using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace SimilarTextureCheckTool
{
    //带数据的TreeViewItem
    public class BatchAssetViewItem : TreeViewItem
    {
        public AssetDescription data;
        public SimilarTextureBatchModifyWindow.BatchData batchData;
        public bool isDoubleMatchTitle;
        public SimilarTextureBatchModifyWindow window;
        /// <summary>
        /// 当设置了图集和源texture文件夹路径后，满足条件的图片在查找匹配时，会反过来查找对应图集的引用情况，在列表列出时也会标蓝
        /// </summary>
        public bool isAtlasRef = false;
    }

    //资源引用树
    public class BatchAssetTreeView : TreeView
    {
        //图标宽度
        const float kIconWidth = 18f;
        //列表高度
        const float kRowHeights = 20f;
        public BatchAssetViewItem assetRoot;

        private GUIStyle stateGUIStyle = new GUIStyle { richText = true, alignment = TextAnchor.MiddleCenter };

        //列信息
        enum MyColumns
        {
            Name,
            Path,
            Operation,
        }

        public BatchAssetTreeView(TreeViewState state,MultiColumnHeader multicolumnHeader):base(state,multicolumnHeader)
        {
            rowHeight = kRowHeights;
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = false;
            customFoldoutYOffset = (kRowHeights - EditorGUIUtility.singleLineHeight) * 0.5f; // center foldout in the row since we also center content. See RowGUI
            extraSpaceBeforeIconAndLabel = kIconWidth;
        }

        //响应右击事件
        protected override void ContextClickedItem(int id)
        {
            SetExpanded(id, !IsExpanded(id));
        }

        //响应双击事件
        protected override void DoubleClickedItem(int id)
        {
            var item = (BatchAssetViewItem)FindItem(id, rootItem);
            //在ProjectWindow中高亮双击资源
            if (item != null)
            {
                var assetObject = AssetDatabase.LoadAssetAtPath(item.data.path, typeof(UnityEngine.Object));
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = assetObject;
                EditorGUIUtility.PingObject(assetObject);
            }
        }
        
        //生成ColumnHeader
        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth)
        {
            var columns = new[]
            {
                //图标+名称
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Name"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 200,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = false,
                    canSort = false        
                },
                //路径
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Path"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 360,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = false,
                    canSort = false
                },
                //操作按钮
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Buttons"),
                    headerTextAlignment = TextAlignment.Center,
                    sortedAscending = false,
                    width = 360,
                    minWidth = 60,
                    autoResize = false,
                    allowToggleVisibility = false,
                    canSort = false          
                },
            };
            var state = new MultiColumnHeaderState(columns);
            return state;
        }

        protected override TreeViewItem BuildRoot()
        {
            return assetRoot;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = (BatchAssetViewItem)args.item;
            for(int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (MyColumns)args.GetColumn(i), ref args);
            }
        }

        //绘制列表中的每项内容
        void CellGUI(Rect cellRect, BatchAssetViewItem item, MyColumns column, ref RowGUIArgs args)
        {
            CenterRectUsingSingleLineHeight(ref cellRect);
            
            // 对于图集节点，需要特殊标注出颜色，以作区分
            if (item.isAtlasRef)
            {
                Color col = GUI.color;
                GUI.color = new Color(35f/255f, 170f/255f, 242f/255f, 1);
                GUI.Box(cellRect, string.Empty);
                GUI.color = col;
            }
            switch (column)
            {
                case MyColumns.Name:
                    var iconRect = cellRect;
                    iconRect.x += GetContentIndent(item);
                    iconRect.width = kIconWidth;
                    if (item.depth > 0 && iconRect.x < cellRect.xMax)
                    {
                        var icon = GetIcon(item.data.path);
                        if(icon != null)
                            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                    }
                    args.rowRect = cellRect;
                    base.RowGUI(args);
                    break;
                case MyColumns.Path:
                    if (item.depth == 0) return;
                    
                    GUI.Label(cellRect, item.data.path);
                    break;
                case MyColumns.Operation:
                    if (item.window == null)
                    {
                        GUI.enabled = true;
                        break;
                    }
                    // 以往用depth来分类节点按钮回调，但是在做单图片替换的差异化时depth不同，因为少了匹配结果这一层
                    // 这里的目的时做了个映射，让单个图片的depth=0时用多图片depth=1的按钮事件，后同
                    int funcType = item.depth;
                    if (item.window.isSingleTexture)
                    {
                        funcType++;
                    }
                    if (funcType == 0) // 如果是类型根节点，则显示全部[基本相同/可能相同]的引用执行替换/回滚操作按钮
                    {
                        DrawBatchOperationBtn(cellRect, item);
                    }
                    else if (funcType == 1) // 如果是图片节点，则显示替换/回滚它的所有引用
                    {
                        DrawItemOperationBtn(cellRect, item);
                    }
                    else if (funcType >= 2) // 此处以下是对应的图片节点的引用情况
                    {
                        DrawRefOperationBtn(cellRect, item);
                    }
                    break;
            }
        }

        //根据资源信息获取资源图标
        private Texture2D GetIcon(string path)
        {
            Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(Object));
            if (obj != null)
            {
                Texture2D icon = AssetPreview.GetMiniThumbnail(obj);
                if (icon == null)
                    icon = AssetPreview.GetMiniTypeThumbnail(obj.GetType());
                return icon;
            }
            return null;
        }

        // 如果是类型根节点，则显示全部执行替换/回滚
        private void DrawBatchOperationBtn(Rect cellRect, BatchAssetViewItem item)
        {
            // 按钮布局计算
            var rectWidth = cellRect.width;
            var buttonRect1 = cellRect;
            buttonRect1.width = rectWidth / 3;
            var buttonRect2 = buttonRect1;
            buttonRect2.x += rectWidth / 3;
            var buttonRect3 = buttonRect2;
            buttonRect3.x += rectWidth / 3;
            
            // 事件
            GUI.enabled = item.window.CanReplaceBatch(item);
            if (GUI.Button(buttonRect1,"此分类全部替换"))
            {
                item.window.ReplaceAllByMatchType(item);
            }
            
            Color oriCol = GUI.backgroundColor;
            GUI.backgroundColor = Color.red;
            if (GUI.Button(buttonRect2,"全部删除替换"))
            {
                bool confirm = EditorUtility.DisplayDialog("二次确认", 
                    "此操作无法回滚，是否确定？", "Ok");
                if (confirm)
                {
                    item.window.ReplaceAllByMatchType(item, true);
                }
            }
            GUI.backgroundColor = oriCol;
            
            GUI.enabled = item.window.CanRevertBatch(item);
            if (GUI.Button(buttonRect3,"此分类全部回滚"))
            {
                item.window.RevertAllByMatchType(item);
            }
            GUI.enabled = true;
        }
        // 如果是图片节点，则显示替换/回滚它的所有引用
        private void DrawItemOperationBtn(Rect cellRect, BatchAssetViewItem item)
        {
            // 按钮布局计算
            var rectWidth = cellRect.width;
            var buttonRect1 = cellRect;
            buttonRect1.width = rectWidth / 3;
            var buttonRect2 = buttonRect1;
            buttonRect2.x += rectWidth / 3;
            var buttonRect3 = buttonRect2;
            buttonRect3.x += rectWidth / 3;
            
            // 事件
            GUI.enabled = item.window.CanReplaceItemAllRefs(item);
            if (GUI.Button(buttonRect1,"全部执行替换"))
            {
                item.window.ReplaceAllByRefItem(item);
            }
            
            Color oriCol = GUI.backgroundColor;
            GUI.backgroundColor = Color.red;
            if (GUI.Button(buttonRect2,"引用删除替换"))
            {
                bool confirm = EditorUtility.DisplayDialog("二次确认", 
                    "此操作无法回滚，是否确定？", "Ok");
                if (confirm)
                {
                    item.window.ReplaceAllByRefItem(item, true);
                }
            }
            GUI.backgroundColor = oriCol;
            
            GUI.enabled = item.window.CanRevertItemAllRefs(item);
            if (GUI.Button(buttonRect3,"全部回滚替换"))
            {
                item.window.RevertAllByRefItem(item);
            }
            GUI.enabled = true;
        }
        // 对应的图片节点的引用控制按钮
        private void DrawRefOperationBtn(Rect cellRect, BatchAssetViewItem item)
        {
            // 按钮布局计算
            var rectWidth = cellRect.width;
            var buttonRect1 = cellRect;
            buttonRect1.width = rectWidth / 2;
            var buttonRect2 = buttonRect1;
            buttonRect2.x += rectWidth / 2;
            
            // 事件
            GUI.enabled = item.window.CanReplaceTarget(item);
            if (GUI.Button(buttonRect1,"执行替换"))
            {
                item.window.ReplaceTarget(item);
            }
            GUI.enabled = item.window.CanRevertTarget(item);
            if (GUI.Button(buttonRect2,"回滚替换"))
            {
                item.window.RevertTarget(item);
            }
            GUI.enabled = true;
        }
    }
}
