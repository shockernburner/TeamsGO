using UnityEngine;

namespace ProjectFossil.Player
{
    // The bought first-person arms: a humanoid rig of which only the arm and sleeve meshes (names containing FPS)
    // are kept. FirstPersonView moves two wrist targets around (rest pose, bob, swing); every frame this bends each
    // arm so its wrist lands on its target, turns the hand to the target's facing and keeps the fingers in a fist.
    //
    // Presentation only.
    public class FirstPersonRig
    {
        // Where the rig's head bone sits relative to the eye camera: a little below and behind the eyes.
        private static readonly Vector3 HeadFromEye = new Vector3(0f, -0.1f, -0.1f);
        // How far the fingers curl (-1..1 on the humanoid finger muscles; negative closes the hand).
        private const float FingerCurl = -0.75f;
        private const float ThumbCurl  = -0.3f;

        private class Arm
        {
            public Transform Upper, Lower, Hand, Index, Middle, Little;
            public Quaternion UpperRest, LowerRest, HandRest;
            public float Side; // +1 right, -1 left
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

            CurlFingers(anim);
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
            return arm.Upper != null && arm.Lower != null && arm.Hand != null ? arm : null;
        }

        private void Remember(Arm a)
        {
            a.UpperRest = a.Upper.localRotation;
            a.LowerRest = a.Lower.localRotation;
            a.HandRest  = a.Hand.localRotation;
        }

        private static void CurlFingers(Animator anim)
        {
            var handler = new HumanPoseHandler(anim.avatar, anim.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            for (int i = 0; i < pose.muscles.Length && i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!n.Contains("Stretched")) continue;
                if (n.Contains("Thumb")) pose.muscles[i] = ThumbCurl;
                else if (n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))
                    pose.muscles[i] = FingerCurl;
            }
            handler.SetHumanPose(ref pose);
            handler.Dispose();
        }

        public void Solve(Transform rightTarget, Transform leftTarget, Transform view)
        {
            Solve(_right, rightTarget, view);
            Solve(_left, leftTarget, view);
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
