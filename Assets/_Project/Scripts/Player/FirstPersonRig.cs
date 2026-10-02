using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Player
{
    // The bought first-person arms: a humanoid rig of which only the arm and sleeve meshes (names containing FPS)
    // are kept. FirstPersonView moves two wrist targets around (relaxed, walking, running, attacking); every frame
    // this bends each arm so its wrist lands on its target, turns the hand to the target's facing and closes the
    // fingers as far as asked: loose when relaxed, a fist around a weapon or for a punch.
    //
    // Presentation only.
    public class FirstPersonRig
    {
        // Where the rig's head bone sits relative to the eye camera: a little below and behind the eyes.
        private static readonly Vector3 HeadFromEye = new Vector3(0f, -0.1f, -0.1f);
        // How far the humanoid finger muscles go for a fist, and how much of that a relaxed hand keeps.
        private const float FistCurl  = 0.8f;
        private const float ThumbCurl = 0.35f;
        private const float Relaxed   = 0.3f;

        private class Arm
        {
            public Transform Upper, Lower, Hand, Index, Middle, Little;
            public Quaternion UpperRest, LowerRest, HandRest;
            public float Side; // +1 right, -1 left
            public Transform[] Fingers;
            public Quaternion[] Open, Fist;
        }

        public Transform Root { get; private set; }
        private Arm _right, _left;

        // Null when the prefab isn't a usable humanoid.
        public static FirstPersonRig Build(GameObject prefab, Transform parent)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = "FirstPersonRig";
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
            {
                Object.Destroy(go);
                return null;
            }

            var rig = new FirstPersonRig { Root = go.transform };
            rig._right = rig.Bones(anim, true);
            rig._left  = rig.Bones(anim, false);
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (rig._right == null || rig._left == null || head == null)
            {
                Debug.LogWarning("[FirstPersonRig] Arm bones not found; keeping the simple arms.");
                Object.Destroy(go);
                return null;
            }

            HandPoses(anim, rig._right, rig._left);
            anim.enabled = false; // nothing animates it but us

            // Only the arms and sleeves; no shadows (the body casts them). The pack has bare and gloved versions of
            // the same arms: when both are switched on, keep the gloves so they don't flicker through each other.
            var all = go.GetComponentsInChildren<Renderer>(true);
            bool gloved = System.Array.Exists(all, r => r.name.Contains("FPS") && r.name.Contains("Gloves") && r.gameObject.activeInHierarchy);
            foreach (var r in all)
            {
                bool bareArms = r.name.Contains("FPS_Arms") && !r.name.Contains("Gloves");
                if (!r.name.Contains("FPS") || (gloved && bareArms)) { r.enabled = false; continue; }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                Drawable(r);
            }

            // Face the camera's way: the shoulders run along +X.
            Vector3 across = parent.InverseTransformPoint(rig._right.Upper.position) - parent.InverseTransformPoint(rig._left.Upper.position);
            across.y = 0f;
            if (across.sqrMagnitude > 1e-6f)
                go.transform.localRotation = Quaternion.FromToRotation(across.normalized, Vector3.right) * go.transform.localRotation;
            // Then put the head just behind the eyes.
            go.transform.localPosition += HeadFromEye - parent.InverseTransformPoint(head.position);

            rig.Remember(rig._right);
            rig.Remember(rig._left);
            return rig;
        }

        // A material this pipeline can't draw renders bright pink. Swap it for a plain lit one with the same
        // texture and colour (setup normally hands over the pack's URP materials, so this is a safety net).
        private static void Drawable(Renderer r)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || (m.shader != null && m.shader.isSupported && !m.shader.name.StartsWith("HDRP")
                                  && m.shader.name != "Hidden/InternalErrorShader")) continue;
                Texture tex = m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap")
                            : m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
                var lit = Placeholder.Lit(col);
                if (tex != null)
                {
                    if (lit.HasProperty("_BaseMap")) lit.SetTexture("_BaseMap", tex);
                    lit.mainTexture = tex;
                }
                lit.name = m.name + " (URP)";
                mats[i] = lit;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }

        private Arm Bones(Animator a, bool right)
        {
            var arm = new Arm
            {
                Upper  = a.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm),
                Lower  = a.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm),
                Hand   = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand),
                Index  = a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal),
                Middle = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal),
                Little = a.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal),
                Side   = right ? 1f : -1f,
            };
            if (arm.Upper == null || arm.Lower == null || arm.Hand == null) return null;

            var fingers = new List<Transform>();
            var first = right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal;
            var last  = right ? HumanBodyBones.RightLittleDistal  : HumanBodyBones.LeftLittleDistal;
            for (var b = first; b <= last; b++)
            {
                var t = a.GetBoneTransform(b);
                if (t != null) fingers.Add(t);
            }
            arm.Fingers = fingers.ToArray();
            return arm;
        }

        private void Remember(Arm a)
        {
            a.UpperRest = a.Upper.localRotation;
            a.LowerRest = a.Lower.localRotation;
            a.HandRest  = a.Hand.localRotation;
        }

        // An open hand and a fist, as finger rotations to blend between. Which sign of the finger muscles closes
        // the hand isn't certain across rigs, so try both and keep whichever brings the fingertips nearer the palm.
        private static void HandPoses(Animator anim, Arm right, Arm left)
        {
            var handler = new HumanPoseHandler(anim.avatar, anim.transform);
            var start = new HumanPose();
            handler.GetHumanPose(ref start);

            float Reach(float sign)
            {
                Pose(handler, start, sign * FistCurl, sign * ThumbCurl);
                var tip = anim.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                return tip != null ? Vector3.Distance(tip.position, right.Hand.position) : 0f;
            }
            float sign = Reach(1f) <= Reach(-1f) ? 1f : -1f;

            Pose(handler, start, sign * FistCurl * Relaxed, sign * ThumbCurl * Relaxed);
            right.Open = Capture(right.Fingers);
            left.Open  = Capture(left.Fingers);
            Pose(handler, start, sign * FistCurl, sign * ThumbCurl);
            right.Fist = Capture(right.Fingers);
            left.Fist  = Capture(left.Fingers);
            handler.Dispose();
        }

        private static void Pose(HumanPoseHandler handler, HumanPose start, float fingers, float thumb)
        {
            var pose = start;
            pose.muscles = (float[])start.muscles.Clone();
            for (int i = 0; i < pose.muscles.Length && i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!n.Contains("Stretched")) continue;
                if (n.Contains("Thumb")) pose.muscles[i] = thumb;
                else if (n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))
                    pose.muscles[i] = fingers;
            }
            handler.SetHumanPose(ref pose);
        }

        private static Quaternion[] Capture(Transform[] bones)
        {
            var q = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) q[i] = bones[i].localRotation;
            return q;
        }

        // grip 0 = relaxed open hand, 1 = closed fist.
        public void Solve(Transform rightTarget, Transform leftTarget, Transform view, float rightGrip, float leftGrip)
        {
            Solve(_right, rightTarget, view);
            Solve(_left, leftTarget, view);
            Grip(_right, rightGrip);
            Grip(_left, leftGrip);
        }

        private static void Grip(Arm a, float k)
        {
            if (a.Open == null || a.Fist == null) return;
            for (int i = 0; i < a.Fingers.Length; i++)
                a.Fingers[i].localRotation = Quaternion.Slerp(a.Open[i], a.Fist[i], k);
        }

        // Two-bone reach: the elbow bends down and out, the wrist lands on the target, the knuckles point along the
        // target's forward with the index finger toward its up (a fist held thumb-up).
        private static void Solve(Arm a, Transform target, Transform view)
        {
            a.Upper.localRotation = a.UpperRest;
            a.Lower.localRotation = a.LowerRest;
            a.Hand.localRotation  = a.HandRest;

            Vector3 shoulder = a.Upper.position;
            float upperLen = Vector3.Distance(shoulder, a.Lower.position);
            float lowerLen = Vector3.Distance(a.Lower.position, a.Hand.position);
            Vector3 toTarget = target.position - shoulder;
            float dist = Mathf.Clamp(toTarget.magnitude, 0.05f, upperLen + lowerLen - 0.001f);
            Vector3 dir = toTarget.normalized;

            Vector3 pole = view.TransformDirection(new Vector3(a.Side * 0.7f, -1f, -0.3f));
            pole = Vector3.ProjectOnPlane(pole, dir).normalized;
            float cosA = Mathf.Clamp((upperLen * upperLen + dist * dist - lowerLen * lowerLen) / (2f * upperLen * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = shoulder + dir * (upperLen * cosA) + pole * (upperLen * sinA);

            a.Upper.rotation = Quaternion.FromToRotation(a.Lower.position - shoulder, elbow - shoulder) * a.Upper.rotation;
            a.Lower.rotation = Quaternion.FromToRotation(a.Hand.position - a.Lower.position, target.position - a.Lower.position) * a.Lower.rotation;

            if (a.Middle != null && a.Index != null && a.Little != null)
            {
                Vector3 knuckles = a.Middle.position - a.Hand.position;
                Vector3 across   = a.Index.position - a.Little.position;
                if (knuckles.sqrMagnitude > 1e-8f && across.sqrMagnitude > 1e-8f)
                {
                    Quaternion now  = Quaternion.LookRotation(knuckles, across);
                    Quaternion want = Quaternion.LookRotation(target.forward, target.up);
                    a.Hand.rotation = want * Quaternion.Inverse(now) * a.Hand.rotation;
                }
            }
        }
    }
}
