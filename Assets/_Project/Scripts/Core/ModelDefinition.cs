using UnityEngine;

namespace ProjectFossil.Core
{
    // A character's look as data: which imported model to show, how to animate it and how big it is.
    // Gameplay never reads this; an empty model means the placeholder body stays visible.
    [CreateAssetMenu(menuName = "Project Fossil/Model Definition", fileName = "Model_New")]
    public class ModelDefinition : ScriptableObject
    {
        public GameObject                model;
        public RuntimeAnimatorController animator;
        [Tooltip("Skinned meshes on the same skeleton (hair, beard, outfits), bound to the model's bones by name")]
        public GameObject[]              attachments;
        [Tooltip("Meshes of the model to cut down to the parts skinned to keepBones (e.g. keep only the head under an outfit)")]
        public string[]                  trimMeshes;
        public string[]                  keepBones;

        [Tooltip("Model is scaled so its bind-pose height matches this, in the owner's local units")]
        public float height = 2f;
        [Tooltip("Extra turn in degrees if the model faces the wrong way")]
        public float yawOffset;
        [Tooltip("Turn the model so the bone named 'Head' points forward (for packs exported facing sideways)")]
        public bool faceHeadForward = true;

        public bool HasModel => model != null;
    }
}
