using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>
    /// Builds the setting scene's object graph: option, menu and title templates kept under an inactive root (so
    /// instantiated copies run Awake like prefabs do) and a setting manager wired to them.
    /// </summary>
    public abstract class SettingSceneTestBase : SceneTestBase
    {
        /// <summary>Gets the canvas holding the scene objects.</summary>
        protected Transform Canvas { get; private set; } = null!;

        /// <summary>Gets the inactive root holding the templates.</summary>
        protected Transform Templates { get; private set; } = null!;

        /// <summary>Gets the option template.</summary>
        protected GameObject OptionTemplate { get; private set; } = null!;

        /// <summary>Gets the menu template.</summary>
        protected GameObject MenuTemplate { get; private set; } = null!;

        /// <summary>Gets the category title template.</summary>
        protected MenuTitleDisplayer TitleTemplate { get; private set; } = null!;

        /// <summary>Gets the description text of the manager.</summary>
        protected TextMeshProUGUI Description { get; private set; } = null!;

        /// <summary>Gets the root under which menus are created.</summary>
        protected GameObject MenuListRoot { get; private set; } = null!;

        /// <summary>Gets the root under which category titles are created.</summary>
        protected GameObject TitleListRoot { get; private set; } = null!;

        /// <summary>Creates the canvas and templates.</summary>
        [SetUp]
        public void SetUpSettingScene()
        {
            Canvas = CreateCanvas();
            Templates = Objects.Create("Templates", Canvas).transform;
            Templates.gameObject.SetActive(false);
            OptionTemplate = CreateOptionTemplate().gameObject;
            MenuTemplate = CreateRect("MenuTemplate", Templates);
            PrivateMember.SetField(MenuTemplate.AddComponent<Menu>(), "_optionPrefab", OptionTemplate);
            TitleTemplate = CreateTitleTemplate();
            Description = CreateText("Description", Canvas);
            MenuListRoot = CreateRect("Menus", Canvas);
            TitleListRoot = CreateRect("Titles", Canvas);
        }

        /// <summary>Creates an option whose name and value texts are its children.</summary>
        /// <param name="parent">The parent; the template root when null.</param>
        /// <returns>The option.</returns>
        protected Option CreateOptionTemplate(Transform? parent = null)
        {
            var root = CreateRect("Option", parent ?? Templates);
            var option = root.AddComponent<Option>();
            PrivateMember.SetField(option, "_nameTextDisplayer", CreateText("Name", root.transform));
            PrivateMember.SetField(option, "_valueTextDisplayer", CreateText("Value", root.transform));
            return option;
        }

        /// <summary>Creates a category title template.</summary>
        /// <returns>The title displayer.</returns>
        private MenuTitleDisplayer CreateTitleTemplate()
        {
            var root = CreateRect("TitleTemplate", Templates);
            var displayer = root.AddComponent<MenuTitleDisplayer>();
            PrivateMember.SetField(displayer, "_unselectedBackground", CreateImage("Unselected", root.transform));
            var selected = CreateImage("Selected", root.transform);
            selected.rectTransform.sizeDelta = new Vector2(300, 80);
            PrivateMember.SetField(displayer, "_selectedBackground", selected);
            PrivateMember.SetField(displayer, "_titleDisplayer", CreateText("Text", root.transform));
            return displayer;
        }

        /// <summary>Creates a setting manager wired to the templates without running its lifecycle.</summary>
        /// <returns>The inactive manager; activate it to run Awake and Start.</returns>
        protected SettingManager CreateManager()
        {
            var manager = AddInactive<SettingManager>("SettingManager", Canvas);
            PrivateMember.SetField(manager, "_menuPrefab", MenuTemplate);
            PrivateMember.SetField(manager, "_menuTitleDisplayerPrefab", TitleTemplate);
            PrivateMember.SetField(manager, "_menuListRoot", MenuListRoot);
            PrivateMember.SetField(manager, "_menuTitleDisplayerListRoot", TitleListRoot);
            PrivateMember.SetField(manager, "_descriptionTextDisplayer", Description);
            return manager;
        }

        /// <summary>Creates an active menu for a settings instance, bound to an inactive manager.</summary>
        /// <param name="manager">The manager supplying the option pool.</param>
        /// <param name="instance">The settings instance shown by the menu.</param>
        /// <returns>The initialized menu.</returns>
        protected Menu CreateMenu(SettingManager manager, object instance)
        {
            var menu = AddInactiveUi<Menu>("Menu", MenuListRoot.transform);
            PrivateMember.SetField(menu, "_optionPrefab", OptionTemplate);
            Activate(menu);
            PrivateMember.SetField(menu, "_manager", manager);
            menu.Instance = instance;
            menu.Name = instance.GetType().Name;
            menu.Init();
            return menu;
        }

        /// <summary>Gets the option currently showing a menu entry.</summary>
        /// <param name="menu">The menu.</param>
        /// <param name="index">The displayed option index.</param>
        /// <returns>The option, or null when it is not visible.</returns>
        protected static Option? VisibleOption(Menu menu, int index)
        {
            return PrivateMember.Invoke(menu, "FindVisibleOption", index) as Option;
        }

        /// <summary>Gets the name text shown by an option.</summary>
        /// <param name="option">The option.</param>
        /// <returns>The name text.</returns>
        protected static string NameOf(Option option)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(option, "_nameTextDisplayer").text;
        }

        /// <summary>Gets the value text shown by an option.</summary>
        /// <param name="option">The option.</param>
        /// <returns>The value text.</returns>
        protected static string ValueOf(Option option)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(option, "_valueTextDisplayer").text;
        }
    }
}
