using MajdataPlay.Rendering;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Tests;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Builds minimal stand-ins for the gameplay prefabs. Every template is created below an inactive root so that no
    /// lifecycle message runs on it; tests instantiate a template into an active parent, which wakes the clone exactly
    /// like a prefab instance, with all serialized references remapped by Unity.
    /// </summary>
    internal static class NoteTemplates
    {
        /// <summary>Creates an empty child game object.</summary>
        /// <param name="name">The object name.</param>
        /// <param name="parent">The parent transform.</param>
        /// <returns>The new object.</returns>
        public static GameObject Child(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        /// <summary>Creates a child game object that carries a sprite renderer.</summary>
        /// <param name="name">The object name.</param>
        /// <param name="parent">The parent transform.</param>
        /// <returns>The renderer.</returns>
        public static SpriteRenderer RendererChild(string name, Transform parent)
        {
            return Child(name, parent).AddComponent<SpriteRenderer>();
        }

        /// <summary>Creates a tap guide line template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject TapLine(Transform root)
        {
            return RendererChild("TapLine", root).gameObject;
        }

        /// <summary>Creates a slide star template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject Star(Transform root)
        {
            return RendererChild("SlideStar", root).gameObject;
        }

        /// <summary>Creates a tap or star note template: the note sprite with its EX overlay as first child.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="tapLine">The guide line template the note instantiates.</param>
        /// <returns>The template.</returns>
        public static GameObject Tap(Transform root, GameObject tapLine)
        {
            var gameObject = Child("Tap", root);
            gameObject.AddComponent<SpriteRenderer>();
            RendererChild("Ex", gameObject.transform);
            var tap = gameObject.AddComponent<TapDrop>();
            PrivateMember.SetField(tap, "_tapLinePrefab", tapLine);
            return gameObject;
        }

        /// <summary>Creates a hold note template: the sliced body sprite, its sliced EX overlay and its end point.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="tapLine">The guide line template the note instantiates.</param>
        /// <returns>The template.</returns>
        public static GameObject Hold(Transform root, GameObject tapLine)
        {
            var gameObject = Child("Hold", root);
            gameObject.AddComponent<SpriteRenderer>().drawMode = SpriteDrawMode.Sliced;
            RendererChild("Ex", gameObject.transform).drawMode = SpriteDrawMode.Sliced;
            RendererChild("EndPoint", gameObject.transform);
            var hold = gameObject.AddComponent<HoldDrop>();
            PrivateMember.SetField(hold, "_tapLinePrefab", tapLine);
            return gameObject;
        }

        /// <summary>Creates a touch note template: point, four fans and the just border.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject Touch(Transform root)
        {
            var gameObject = Child("Touch", root);
            RendererChild("Point", gameObject.transform);
            for (var i = 0; i < 4; i++)
            {
                RendererChild($"Fan{i}", gameObject.transform);
            }
            RendererChild("JustBorder", gameObject.transform);
            gameObject.AddComponent<TouchDrop>();
            return gameObject;
        }

        /// <summary>Creates a touch hold note template: progress border, four fans and the point.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject TouchHold(Transform root)
        {
            var gameObject = Child("TouchHold", root);
            Child("Border", gameObject.transform).AddComponent<RawSpriteRenderer>();
            for (var i = 0; i < 4; i++)
            {
                RendererChild($"Fan{i}", gameObject.transform);
            }
            RendererChild("Point", gameObject.transform);
            gameObject.AddComponent<TouchHoldDrop>();
            return gameObject;
        }

        /// <summary>Creates an each line template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject EachLine(Transform root)
        {
            var gameObject = Child("EachLine", root);
            gameObject.AddComponent<SpriteRenderer>();
            gameObject.AddComponent<EachLineDrop>();
            return gameObject;
        }

        /// <summary>Adds the slide judgment result object as a child.</summary>
        /// <param name="parent">The slide that owns it.</param>
        /// <returns>The slide result component.</returns>
        public static SlideOK AddSlideOK(Transform parent)
        {
            var gameObject = Child("SlideOK", parent);
            gameObject.AddComponent<SpriteRenderer>();
            gameObject.AddComponent<Animator>();
            return gameObject.AddComponent<SlideOK>();
        }

        /// <summary>Creates a predefined slide template whose bars run from key 1 towards the end key.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="slideType">The slide table name, for example line3.</param>
        /// <param name="barCount">The number of arrow bars.</param>
        /// <param name="endPos">The end key used to lay out the bars.</param>
        /// <param name="star">The slide star template.</param>
        /// <returns>The template.</returns>
        public static GameObject Slide(Transform root, string slideType, int barCount, int endPos, GameObject star)
        {
            var gameObject = Child($"Slide_{slideType}", root);
            AddBars(gameObject.transform, barCount, endPos);
            AddSlideOK(gameObject.transform);
            var slide = gameObject.AddComponent<SlideDrop>();
            PrivateMember.SetField(slide, "SlideStarPrefab", star);
            PrivateMember.SetField(slide, "_slideType", slideType);
            return gameObject;
        }

        /// <summary>Creates a wifi slide template with eleven fan bars.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="star">The slide star template.</param>
        /// <returns>The template.</returns>
        public static GameObject Wifi(Transform root, GameObject star)
        {
            var gameObject = Child("Slide_Wifi", root);
            AddBars(gameObject.transform, 11, 5);
            AddSlideOK(gameObject.transform);
            var wifi = gameObject.AddComponent<WifiDrop>();
            PrivateMember.SetField(wifi, "SlideStarPrefab", star);
            return gameObject;
        }

        /// <summary>Creates an extend slide template, which only owns its slide result object up front.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="star">The slide star template.</param>
        /// <returns>The template.</returns>
        public static GameObject ExtendSlide(Transform root, GameObject star)
        {
            var gameObject = Child("Slide_Extend", root);
            AddSlideOK(gameObject.transform);
            var slide = gameObject.AddComponent<ExtendSlideDrop>();
            PrivateMember.SetField(slide, "SlideStarPrefab", star);
            return gameObject;
        }

        /// <summary>Creates the multiple-touch border template with its two stacked renderers.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject TouchBorder(Transform root)
        {
            var gameObject = Child("TouchBorder", root);
            RendererChild("Two", gameObject.transform);
            RendererChild("Three", gameObject.transform);
            gameObject.AddComponent<TouchBorder>();
            return gameObject;
        }

        /// <summary>Creates a tap judgment effect template (also used for touch hold judgment effects).</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="isTouchHold">Whether to use the touch hold variant of the displayer.</param>
        /// <returns>The template.</returns>
        public static GameObject TapEffect(Transform root, bool isTouchHold)
        {
            var gameObject = Child(isTouchHold ? "TouchHoldEffect" : "TapEffect", root);
            TapEffectDisplayer displayer = isTouchHold
                ? gameObject.AddComponent<TouchHoldEffectDisplayer>()
                : gameObject.AddComponent<TapEffectDisplayer>();
            var effectParent = Child("Effects", gameObject.transform);
            var perfect = Animated<TapPerfectEffectDisplayer>("Perfect", effectParent.transform);
            var great = Animated<TapGreatEffectDisplayer>("Great", effectParent.transform);
            var good = Animated<TapGoodEffectDisplayer>("Good", effectParent.transform);
            PrivateMember.SetField(displayer, "effectParent", effectParent);
            PrivateMember.SetField(displayer, "_perfectDisplayer", perfect);
            PrivateMember.SetField(displayer, "_greatDisplayer", great);
            PrivateMember.SetField(displayer, "_goodDisplayer", good);
            PrivateMember.SetField(displayer, "JudgeTextDisplayer", JudgeText(gameObject.transform));
            PrivateMember.SetField(displayer, "_fastLateDisplayerA", FastLate("FastLateA", gameObject.transform));
            PrivateMember.SetField(displayer, "_fastLateDisplayerB", FastLate("FastLateB", gameObject.transform));
            return gameObject;
        }

        /// <summary>Creates a touch judgment effect template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject TouchEffect(Transform root)
        {
            var gameObject = Child("TouchEffect", root);
            var displayer = gameObject.AddComponent<TouchEffectDisplayer>();
            PrivateMember.SetField(displayer, "_judgeEffectDisplayer", Animated<TouchJudgeEffectDisplayer>("JudgeEffect", gameObject.transform));
            PrivateMember.SetField(displayer, "_judgeTextDisplayer", JudgeText(gameObject.transform));
            PrivateMember.SetField(displayer, "_fastLateDisplayerA", FastLate("FastLateA", gameObject.transform));
            PrivateMember.SetField(displayer, "_fastLateDisplayerB", FastLate("FastLateB", gameObject.transform));
            return gameObject;
        }

        /// <summary>Creates a hold particle effect template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <param name="material">The material assigned to the particle renderer.</param>
        /// <returns>The template.</returns>
        public static GameObject HoldEffect(Transform root, Material material)
        {
            var gameObject = Child("HoldEffect", root);
            gameObject.AddComponent<ParticleSystem>();
            gameObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            gameObject.AddComponent<HoldEffectDisplayer>();
            return gameObject;
        }

        /// <summary>Creates a touch feedback effect template.</summary>
        /// <param name="root">The inactive template root.</param>
        /// <returns>The template.</returns>
        public static GameObject TouchFeedback(Transform root)
        {
            var gameObject = Child("TouchFeedback", root);
            var animator = gameObject.AddComponent<Animator>();
            RendererChild("Sprite", gameObject.transform);
            var displayer = gameObject.AddComponent<TouchFeedbackDisplayer>();
            PrivateMember.SetField(displayer, "_anim", animator);
            return gameObject;
        }

        /// <summary>Creates a firework effect object with its three renderers.</summary>
        /// <param name="parent">The parent transform; an inactive parent defers Awake.</param>
        /// <returns>The firework displayer.</returns>
        public static FireworkDisplayer Firework(Transform parent)
        {
            var gameObject = Child("Firework", parent);
            var animator = gameObject.AddComponent<Animator>();
            var colorBall = RendererChild("ColorBall", gameObject.transform);
            var firework = RendererChild("Firework", gameObject.transform);
            var bigColorBall = RendererChild("BigColorBall", gameObject.transform);
            var displayer = gameObject.AddComponent<FireworkDisplayer>();
            PrivateMember.SetField(displayer, "_colorBallRenderer", colorBall);
            PrivateMember.SetField(displayer, "_fireworkRenderer", firework);
            PrivateMember.SetField(displayer, "_bigColorBallRenderer", bigColorBall);
            PrivateMember.SetField(displayer, "_animator", animator);
            return displayer;
        }

        /// <summary>Creates a judgment text displayer with its text and break shine renderers.</summary>
        /// <param name="parent">The parent transform.</param>
        /// <returns>The displayer.</returns>
        public static JudgeTextDisplayer JudgeText(Transform parent)
        {
            var gameObject = Child("JudgeText", parent);
            gameObject.AddComponent<Animator>();
            var text = RendererChild("Text", gameObject.transform);
            var shine = RendererChild("Break", gameObject.transform);
            var displayer = gameObject.AddComponent<JudgeTextDisplayer>();
            PrivateMember.SetField(displayer, "textRenderer", text);
            PrivateMember.SetField(displayer, "breakRenderer", shine);
            return displayer;
        }

        /// <summary>Creates a fast/late displayer with its text renderer.</summary>
        /// <param name="name">The object name.</param>
        /// <param name="parent">The parent transform.</param>
        /// <returns>The displayer.</returns>
        public static FastLateDisplayer FastLate(string name, Transform parent)
        {
            var gameObject = Child(name, parent);
            gameObject.AddComponent<Animator>();
            var text = RendererChild("Text", gameObject.transform);
            var displayer = gameObject.AddComponent<FastLateDisplayer>();
            PrivateMember.SetField(displayer, "textRenderer", text);
            return displayer;
        }

        /// <summary>Creates an animated effect object with one sprite child.</summary>
        /// <typeparam name="T">The effect component type.</typeparam>
        /// <param name="name">The object name.</param>
        /// <param name="parent">The parent transform.</param>
        /// <returns>The effect component.</returns>
        public static T Animated<T>(string name, Transform parent) where T : Component
        {
            var gameObject = Child(name, parent);
            gameObject.AddComponent<Animator>();
            RendererChild("Sprite", gameObject.transform);
            return gameObject.AddComponent<T>();
        }

        /// <summary>Adds arrow bars between the tap positions of key 1 and the end key.</summary>
        /// <param name="parent">The slide transform.</param>
        /// <param name="barCount">The number of bars.</param>
        /// <param name="endPos">The end key.</param>
        private static void AddBars(Transform parent, int barCount, int endPos)
        {
            var start = NoteHelper.GetTapPosition(1, 4.8f);
            var end = NoteHelper.GetTapPosition(endPos, 4.8f);
            for (var i = 0; i < barCount; i++)
            {
                var bar = RendererChild($"Bar{i}", parent);
                bar.transform.localPosition = Vector3.Lerp(start, end, (i + 1f) / (barCount + 1f));
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, -90f - (i * 2f));
            }
        }
    }
}
