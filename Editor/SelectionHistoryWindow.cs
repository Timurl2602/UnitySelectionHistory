using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SelectionHistoryWindow
{
    public class SelectionHistoryWindow : EditorWindow
    {
        private List<Object> selectionHistory = new();
        private ScrollView historyScrollView;

        private Toggle trackHierarchyToggle;
        private Toggle trackProjectToggle;

        private TextField searchField;
        private string searchQuery = "";

        private const string TrackHierarchyPref = "SelectionHistory.TrackHierarchy";
        private const string TrackProjectPref = "SelectionHistory.TrackProject";

        [MenuItem("Window/Selection History Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<SelectionHistoryWindow>();
            window.titleContent = new GUIContent("Selection History Window");
        }

        private void CreateGUI()
        {
            var visualTree =
                EditorGUIUtility.Load(
                        "Packages/com.timurluedemann.selectionhistorywindow/Editor/UI/SelectionHistoryWindowUXML.uxml")
                    as
                    VisualTreeAsset;
            if (visualTree != null)
            {
                var root = visualTree.CloneTree();
                rootVisualElement.Add(root);

                var styleSheet =
                    EditorGUIUtility.Load(
                            "Packages/com.timurluedemann.selectionhistorywindow/Editor/UI/SelectionHistoryWindowUSS.uss")
                        as
                        StyleSheet;
                if (styleSheet != null)
                    rootVisualElement.styleSheets.Add(styleSheet);

                historyScrollView = root.Q<ScrollView>("history-container");
                var clearButton = root.Q<Button>("clear-button");
                trackHierarchyToggle = root.Q<Toggle>("track-hierarchy-toggle");
                trackProjectToggle = root.Q<Toggle>("track-project-toggle");
                searchField = root.Q<TextField>("search-field");

                clearButton.tooltip = "Clear selection history";
                trackHierarchyToggle.tooltip = "Track scene object selections (Hierarchy)";
                trackProjectToggle.tooltip = "Track asset selections (Project)";
                searchField.tooltip = "Filter history by name or path";

                trackHierarchyToggle.value = EditorPrefs.GetBool(TrackHierarchyPref, true);
                trackProjectToggle.value = EditorPrefs.GetBool(TrackProjectPref, true);

                trackHierarchyToggle.RegisterValueChangedCallback(evt =>
                {
                    EditorPrefs.SetBool(TrackHierarchyPref, evt.newValue);
                    RefreshHistoryUI();
                });

                trackProjectToggle.RegisterValueChangedCallback(evt =>
                {
                    EditorPrefs.SetBool(TrackProjectPref, evt.newValue);
                    RefreshHistoryUI();
                });

                searchField.RegisterValueChangedCallback(evt =>
                {
                    searchQuery = evt.newValue.Trim().ToLowerInvariant();
                    RefreshHistoryUI();
                });

                clearButton.clicked += () =>
                {
                    selectionHistory.Clear();
                    RefreshHistoryUI();
                };

                Selection.selectionChanged += OnSelectionChanged;
            }
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            var selected = Selection.activeObject;
            if (selected == null) return;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            bool isAsset = !string.IsNullOrEmpty(assetPath);
            bool isSceneObject = selected is GameObject && !isAsset;

            if (isSceneObject && !trackHierarchyToggle.value) return;
            if (isAsset && !trackProjectToggle.value) return;

            if (selectionHistory.Count > 0 && selectionHistory[0] == selected) return;

            selectionHistory.Remove(selected);
            selectionHistory.Insert(0, selected);

            if (selectionHistory.Count > 50)
                selectionHistory.RemoveAt(selectionHistory.Count - 1);

            RefreshHistoryUI();
        }

        private void RefreshHistoryUI()
        {
            if (historyScrollView == null)
                return;

            historyScrollView.Clear();

            for (int i = 0; i < selectionHistory.Count; i++)
            {
                var obj = selectionHistory[i];
                if (obj == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(obj);
                bool isAsset = !string.IsNullOrEmpty(assetPath);
                string displayName = isAsset ? Path.GetFileName(assetPath) : obj.name;

                // Search filter
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    if (!displayName.ToLowerInvariant().Contains(searchQuery) &&
                        !assetPath.ToLowerInvariant().Contains(searchQuery))
                        continue;
                }

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.AddToClassList("history-item");
                row.AddToClassList(i % 2 == 0 ? "history-item-even" : "history-item-odd");

                Texture icon = EditorGUIUtility.ObjectContent(obj, obj.GetType()).image;
                var image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                image.style.width = 32;
                image.style.height = 32;
                image.style.marginRight = 12;

                var nameLabel = new Label(displayName)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginRight = 6
                    }
                };

                var pathLabel = new Label(isAsset ? assetPath : "(Scene Object)")
                {
                    style =
                    {
                        color = new Color(0.8f, 0.8f, 0.8f, 0.8f),
                        unityFontStyleAndWeight = FontStyle.Italic,
                        fontSize = 10,
                        flexGrow = 1
                    }
                };

                var nameContainer = new VisualElement();
                nameContainer.style.flexDirection = FlexDirection.Column;
                nameContainer.AddToClassList("name-container");
                nameContainer.Add(nameLabel);
                nameContainer.Add(pathLabel);

                row.Add(image);
                row.Add(nameContainer);

                // Left click
                row.RegisterCallback<MouseUpEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                });

                // Right click menu
                row.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    if (isAsset)
                    {
                        evt.menu.AppendAction("Open Asset", _ => AssetDatabase.OpenAsset(obj));
                        evt.menu.AppendAction("Show in Explorer", _ => EditorUtility.RevealInFinder(assetPath));
                    }

                    evt.menu.AppendAction("Copy GUID", _ =>
                    {
                        string guid = AssetDatabase.AssetPathToGUID(assetPath);
                        EditorGUIUtility.systemCopyBuffer = guid;
                    });

                    evt.menu.AppendAction("Copy Name", _ => { EditorGUIUtility.systemCopyBuffer = displayName; });

                    evt.menu.AppendAction("Copy Path", _ => { EditorGUIUtility.systemCopyBuffer = assetPath; });

                    evt.menu.AppendAction("Remove from History", _ =>
                    {
                        selectionHistory.Remove(obj);
                        RefreshHistoryUI();
                    });
                }));

                historyScrollView.Add(row);
            }
        }
    }
}
