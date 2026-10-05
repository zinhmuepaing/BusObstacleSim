using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Builds the Animator controllers for the imported characters. All Kenney characters share one
    /// skeleton, so clips from one character drive all of them.
    /// </summary>
    internal static class CharacterAnimation
    {
        public const string AnimationFolder = "Assets/_Project/Animation";
        private const string PersonControllerPath = AnimationFolder + "/Person.controller";
        private const string RiderControllerPath = AnimationFolder + "/Rider.controller";
        private const string SourceCharacter = "character-male-a";

        // Walk and sprint speeds (m/s) at which each clip plays at its natural pace.
        private const float WalkBlendSpeed = 1.4f;
        private const float SprintBlendSpeed = 3f;
        private const float FallTransitionSeconds = 0.05f;

        private static readonly string[] LoopingClips = { "idle", "walk", "sprint", "drive", "sit" };

        public static RuntimeAnimatorController PersonController()
        {
            return LoadOrBuild(PersonControllerPath, BuildPersonController);
        }

        public static RuntimeAnimatorController RiderController()
        {
            return LoadOrBuild(RiderControllerPath, BuildRiderController);
        }

        private static RuntimeAnimatorController LoadOrBuild(string path, System.Func<string, AnimatorController> build)
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                return existing;
            }
            EditorAssetUtil.EnsureFolder(AnimationFolder);
            EnsureLoops();
            return build(path);
        }

        /// <summary>Marks the idle, walk, sprint and seated clips as looping in the source character.</summary>
        private static void EnsureLoops()
        {
            string path = ModelLibrary.Person(SourceCharacter);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            bool changed = false;
            for (int i = 0; i < clips.Length; i++)
            {
                bool wantLoop = System.Array.IndexOf(LoopingClips, clips[i].name) >= 0;
                if (clips[i].loopTime != wantLoop)
                {
                    clips[i].loopTime = wantLoop;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static AnimationClip Clip(string clipName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelLibrary.Person(SourceCharacter)))
            {
                if (asset is AnimationClip clip && clip.name == clipName)
                {
                    return clip;
                }
            }
            Debug.LogWarning($"BusSim: animation clip '{clipName}' not found in {SourceCharacter}.");
            return null;
        }

        private static AnimatorController BuildPersonController(string path)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Fall", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            BlendTree tree = new BlendTree
            {
                name = "Move",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(Clip("idle"), 0f);
            tree.AddChild(Clip("walk"), WalkBlendSpeed);
            tree.AddChild(Clip("sprint"), SprintBlendSpeed);

            AnimatorState move = machine.AddState("Move");
            move.motion = tree;
            machine.defaultState = move;

            AnimatorState fall = machine.AddState("Fall");
            fall.motion = Clip("fall");
            AnimatorStateTransition toFall = machine.AddAnyStateTransition(fall);
            toFall.AddCondition(AnimatorConditionMode.If, 0f, "Fall");
            toFall.hasExitTime = false;
            toFall.duration = FallTransitionSeconds;
            toFall.canTransitionToSelf = false;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorController BuildRiderController(string path)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState seated = machine.AddState("Seated");
            seated.motion = Clip("drive");
            machine.defaultState = seated;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
