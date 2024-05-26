using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;

namespace SimilarTextureCheckTool
{
    public enum SimilarTextureCheckSubTitleType
    {
        SingleTexture = 0,
        Folder = 1
    }
    public class SimilarTextureCheckWindow : EditorWindow
    {
        /// <summary>
        /// 已经缓存下来的图片特征值资产文件
        /// </summary>
        public SimilarTextureCacheAsset cacheSimilarData;

        /// <summary>
        /// 临时存储的图片特征值数据
        /// </summary>
        public Dictionary<string, SimilarTextureCacheData> tempCacheSimilarDict = new Dictionary<string, SimilarTextureCacheData>();
        
        /// <summary>
        /// 更新缓存时用来临时存储移动资源的数据
        /// </summary>
        private Dictionary<string, string> mergeMovedPathsMap = new Dictionary<string, string>();
        /// <summary>
        /// 选项卡id 
        /// </summary>
        private SimilarTextureCheckSubTitleType subtitleIndex = SimilarTextureCheckSubTitleType.SingleTexture;

        #region 对比所需属性
        public double ImageHistogramThreshold = SimilarTextureCheckToolUtil.ImageHistogramThresholdDefault;
        public int PerceptualHashThreshold = SimilarTextureCheckToolUtil.PerceptualHashThresholdDefault;
        
        private string limitCompareTextureFolderPath;
        private string atlasTextureFolderPath;
        private string sourceTextureFolderPath;
        private DefaultAsset limitCompareTextureFolderAsset;
        private DefaultAsset atlasTextureRootFolderAsset;
        private DefaultAsset sourceTextureRootFolderAsset;

        private bool showDoubleMatch = true;
        private bool showSingleMatch = true;

        // 当前要查看的贴图资产和特征值数据
        private Texture2D curCheckTex;
        private Vector2 resultScrollPos = Vector2.zero;

        private DefaultAsset curFolderAsset;
        private List<Texture2D> limitFolderTexList = new List<Texture2D>();
        private List<string> limitFolderTexPathList = new List<string>();
        private List<Texture2D> checkTexList = new List<Texture2D>();
        private List<string> checkTexPathList = new List<string>();
        private List<bool> checkTexDeleteMarkList = new List<bool>();
        private List<Vector2> checkResultScrollRectPos = new List<Vector2>();
        /// <summary>
        /// 缩略图尺寸
        /// </summary>
        public int previewTexSize = 130;
        /// <summary>
        /// 当遍历进行时为true
        /// </summary>
        private bool CollectingCacheData = false;
        /// <summary>
        /// 匹配后收集到的相似图片数据集合
        /// </summary>
        private List<List<SimilarTextureCacheData>> totalDoubleMatchedSimilarTextures = new List<List<SimilarTextureCacheData>>();
        /// <summary>
        /// 匹配后收集到的可能相似图片数据集合
        /// </summary>
        private List<List<SimilarTextureCacheData>> totalSingleMatchedSimilarTextures = new List<List<SimilarTextureCacheData>>();
        /// <summary>
        /// 单感知哈希值匹配满足列表
        /// </summary>
        private List<List<SimilarTextureCacheData>> P_TotalMatchedSimilarTextures = new List<List<SimilarTextureCacheData>>();
        /// <summary>
        /// 单直方图匹配满足列表
        /// </summary>
        private List<List<SimilarTextureCacheData>> H_TotalMatchedSimilarTextures = new List<List<SimilarTextureCacheData>>();
        #endregion

        #region 编辑器Update相关
        private int checkTexListIndex = 0; // 当前遍历到的m_checkTexList的索引值
        private int updateIndex = 0;
        private int updateTotalNum = 0;
        private string[] allAssetPaths;
        /// <summary>
        /// 计算过滤到的图片路径集合
        /// </summary>
        private List<string> texturePaths = new List<string>();
        private List<List<string>> checkTexturesFoundResultList = new List<List<string>>();
        #endregion

        #region Styles
        private bool initedStyles = false;
        private GUIStyle headTitleStyle;
        private GUIStyle spriteTipsStyle;
        private GUIStyle doubleMatchTitleStyle;
        private GUIStyle singleMatchTitleStyle;
        #endregion

        #region 操作弹窗相关
        private List<SimilarTextureBatchModifyWindow> batchModifySubWindowList = new List<SimilarTextureBatchModifyWindow>();
        private bool initedReferenceFinderData = false;
        private ReferenceFinderData referenceFinderData = new ReferenceFinderData();
        #endregion
        
        [MenuItem("SimilarTextureCheck/Open Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<SimilarTextureCheckWindow>();
            window.titleContent = new GUIContent("相似图片检查/定位工具面板");
            window.minSize = new Vector2(800, 700);
            window.maxSize = new Vector2(1400, 1600);
            window.InitStyles();
            window.LoadPrefs();
            window.LoadCacheData();
        }
        private void InitStyles()
        {
            if (!initedStyles)
            {
                headTitleStyle = new GUIStyle("HeaderLabel");
                headTitleStyle.fontSize = 20;
                spriteTipsStyle = EditorStyles.helpBox;
                spriteTipsStyle.fontSize = 14;
                doubleMatchTitleStyle = new GUIStyle("ProfilerSelectedLabel");
                doubleMatchTitleStyle.fontSize = 16;
                singleMatchTitleStyle = new GUIStyle("ProfilerSelectedLabel");
                singleMatchTitleStyle.fontSize = 16;
                initedStyles = true;
            }
        }

        private void LoadPrefs()
        {
            LoadFolderPrefByPath(SimilarTextureCheckToolUtil.LimitCompareTexturePathName, ref limitCompareTextureFolderPath, ref limitCompareTextureFolderAsset);
            LoadFolderPrefByPath(SimilarTextureCheckToolUtil.AtlasTextureRootPathName, ref atlasTextureFolderPath, ref atlasTextureRootFolderAsset);
            LoadFolderPrefByPath(SimilarTextureCheckToolUtil.SourceTextureRootPathName, ref sourceTextureFolderPath, ref sourceTextureRootFolderAsset);
            ImageHistogramThreshold = (double)EditorPrefs.GetFloat(SimilarTextureCheckToolUtil.ImageHistogramThresholdKey, (float)ImageHistogramThreshold);
            PerceptualHashThreshold = EditorPrefs.GetInt(SimilarTextureCheckToolUtil.PerceptualHashThresholdKey, PerceptualHashThreshold);
        }

        private void LoadFolderPrefByPath(string prefKey, ref string path, ref DefaultAsset folderAsset)
        {
            path = EditorPrefs.GetString(prefKey);
            if (!string.Empty.Equals(path))
            {
                folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            }
        }

        // 加载缓存数据
        private void LoadCacheData()
        {
            if (File.Exists(SimilarTextureCheckToolUtil.CacheDataSavePath))
            {
                //反序列化数据
                FileStream fs = File.OpenRead(SimilarTextureCheckToolUtil.CacheDataSavePath);
                try
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    EditorUtility.DisplayProgressBar("正在导入缓存图片特征值信息", "读取中...", 0);
                    try
                    {
                        List<SimilarTextureCacheData> cacheList = (List<SimilarTextureCacheData>) bf.Deserialize(fs);
                        cacheSimilarData = CreateInstance<SimilarTextureCacheAsset>();
                        cacheSimilarData.savedCacheDataList = cacheList;
                        cacheSimilarData.Init();
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }
                finally
                {
                    fs.Close();
                }
            }
        }
        
        #region 界面布局逻辑

        private void OnGUI()
        {
            InitStyles();
            // 检查全局图片资源
            DrawCachePart();
            // 绘制工具tips等内容
            DrawTipInfos();
            // 绘制限制比对范围文件夹
            DrawTargetFolder();
            // 选项卡绘制
            DrawSubTitleButton();
            // 绘制主体内容
            if (subtitleIndex == SimilarTextureCheckSubTitleType.SingleTexture) // 单图片对比
            {
                DrawSingleTextureComparisonContent();
            }
            else if (subtitleIndex == SimilarTextureCheckSubTitleType.Folder) // 文件夹拖拽对比
            {
                DrawFolderComparisonContent();
            }
            // 列出比对结果
            DrawSimilarTextureResult();
        }

        /// <summary>
        /// 绘制检查所有图片资源相关控件
        /// </summary>
        private void DrawCachePart()
        {
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("一键更新项目中所有图片特征值Hash（耗时很长，慎点！！！）", GUILayout.Height(25)))
            {
                bool option = EditorUtility.DisplayDialog("二次确认", "真的会用很长时间的哦，是否确定？" , "Go!!");
                if(option)
                    CheckALLTextureResourcesAndCacheHashAndHistogramData();
            }
            EditorGUILayout.Space();
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("更新已有缓存", GUILayout.Height(25)) && cacheSimilarData != null)
            {
                LoadModifyPathsSaveCacheAndUpdateData();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space();
        }

        private void DrawTargetFolder()
        {
            EditorGUILayout.LabelField("限制对比范围", headTitleStyle, GUILayout.Height(40));
            EditorGUILayout.LabelField("如果设置了此文件夹，则会预计算这个文件夹内的图片特征信息，并只对这个文件夹内的图片进行相似性比对");
            DrawTipsRootFolder("指定对比相似图片的文件夹", SimilarTextureCheckToolUtil.LimitCompareTexturePathName, 
                ref limitCompareTextureFolderAsset, ref limitCompareTextureFolderPath);
            EditorGUILayout.Space();
        }
        /// <summary>
        /// 绘制工具tips内容，以及相关文件夹注册的GUI控件
        /// </summary>
        private void DrawTipInfos()
        {
            EditorGUILayout.LabelField("图集相关设置", headTitleStyle, GUILayout.Height(40));
#if UNITY_2020_1_OR_NEWER
            EditorGUILayout.LabelField(new GUIContent(SimilarTextureCheckToolUtil.SpriteWarningTips, 
                EditorGUIUtility.IconContent("console.warnicon@2x").image), spriteTipsStyle);
#else
            EditorGUILayout.HelpBox(SimilarTextureCheckToolUtil.SpriteWarningTips, MessageType.Warning);
#endif
            EditorGUILayout.BeginHorizontal();
            DrawTipsRootFolder("图集根目录", SimilarTextureCheckToolUtil.AtlasTextureRootPathName, 
                ref atlasTextureRootFolderAsset, ref atlasTextureFolderPath);
            DrawTipsRootFolder("源texture根目录", SimilarTextureCheckToolUtil.SourceTextureRootPathName, 
                ref sourceTextureRootFolderAsset, ref sourceTextureFolderPath);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DrawTipsRootFolder(string header, string prefKey, ref DefaultAsset folderAsset, ref string cachePath)
        {
            EditorGUI.BeginChangeCheck();
            var newFolderAsset = EditorGUILayout.ObjectField(header, folderAsset, typeof(DefaultAsset), false) as DefaultAsset;
            if (EditorGUI.EndChangeCheck())
            {
                if (newFolderAsset == null) // 允许置空
                {
                    EditorPrefs.SetString(prefKey, string.Empty);
                    folderAsset = null;
                }
                else
                {
                    var newFolderPath = AssetDatabase.GetAssetPath(newFolderAsset);
                    if (SimilarTextureCheckToolUtil.IsFolderAsset(newFolderPath)) // 检查传递进来的必须是一个文件夹对象
                    {
                        cachePath = newFolderPath;
                        EditorPrefs.SetString(prefKey, newFolderPath);
                        folderAsset = newFolderAsset;
                    }
                }
            }
        }

        private void DrawSubTitleButton()
        {
            GUI.backgroundColor = Color.green;
            EditorGUI.BeginChangeCheck();
            int newType = GUILayout.Toolbar((int)subtitleIndex, new string[] { "单图片对比", "文件夹对比" },
                GUILayout.Height(25));
            if(EditorGUI.EndChangeCheck())
            {
                ClearAllCacheData();
                subtitleIndex = (SimilarTextureCheckSubTitleType)newType;
            }
            GUI.backgroundColor = Color.white;
        }

        /// <summary>
        /// 绘制目标检查的图片，以及根据缓存刷新它对应的可能重复的图片资产
        /// </summary>
        private void DrawSingleTextureComparisonContent()
        {
            EditorGUILayout.LabelField("全局查找相似Texture", headTitleStyle, GUILayout.Height(40));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("传入要对比的贴图");
            curCheckTex = EditorGUILayout.ObjectField(curCheckTex, typeof(Texture2D), false) as Texture2D;
            if (curCheckTex == null) return;
            DrawSizeAndThresholdSettingContent();
            
            if(GUILayout.Button("开始检索图片的相似贴图"))
            {
                TryGetTargetTextureSimilarTextures();
            }
            EditorGUILayout.Space();
        }
        
        private void DrawFolderComparisonContent()
        {
            EditorGUILayout.LabelField("全局查找相似Texture", headTitleStyle, GUILayout.Height(40));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("传入要对比的贴图文件夹");
            curFolderAsset = EditorGUILayout.ObjectField(curFolderAsset, typeof(DefaultAsset), false) as DefaultAsset;
            if (curFolderAsset == null) return;
            DrawSizeAndThresholdSettingContent();
            if(GUILayout.Button("开始检索文件夹内图片的相似贴图"))
            {
                TryGetTargetFolderSimilarTextures();
            }
            EditorGUILayout.Space();
        }

        private void DrawSizeAndThresholdSettingContent()
        {
            previewTexSize = EditorGUILayout.IntSlider("缩略图尺寸", previewTexSize, 60, 200);
            
            EditorGUI.BeginChangeCheck();
            ImageHistogramThreshold = (double)EditorGUILayout.Slider(
                new GUIContent("直方图匹配阈值", "越高代表颜色分布越相似，推荐值0.90~0.98，默认值0.9"), 
                (float)ImageHistogramThreshold, 0.8f, 0.99f);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(SimilarTextureCheckToolUtil.ImageHistogramThresholdKey, (float)ImageHistogramThreshold);
            }
            
            EditorGUI.BeginChangeCheck();
            PerceptualHashThreshold = EditorGUILayout.IntSlider(
                new GUIContent("感知复杂度匹配阈值", "越小代表图片低频区域对比越严格，推荐值0~3，默认值3"), 
                PerceptualHashThreshold, 0, 7);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(SimilarTextureCheckToolUtil.PerceptualHashThresholdKey, PerceptualHashThreshold);
            }
        }

        // 优化版本：将加载内容改成固定位置控件的方式创建
        private void DrawSimilarTextureResult()
        {
            // 用此标记记录当前是否正在遍历中，如果未遍历或已完成，再刷新面板
            if (CollectingCacheData || checkTexList.Count <= 0) return;
            
            // 选择显示内容
            EditorGUILayout.BeginHorizontal();
            var defaultWidth = EditorGUIUtility.labelWidth; 
            EditorGUIUtility.labelWidth = 180;
            showDoubleMatch = EditorGUILayout.Toggle(new GUIContent("显示满足所有相似度算法的图片"), showDoubleMatch);
            showSingleMatch = EditorGUILayout.Toggle(new GUIContent("显示满足单个相似度算法的图片"), showSingleMatch);
            EditorGUIUtility.labelWidth = defaultWidth;
            EditorGUILayout.EndHorizontal();
            
            // 加载滚动列表，这部分改成固定坐标控件来实现
            Rect lastRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float totalHeight = 0;
            for (int k = 0; k < checkTexList.Count; k++)
            {
                totalHeight += CalCheckingTextureResultsItemHeight(k) + 5;
            }
            resultScrollPos = GUI.BeginScrollView(lastRect, resultScrollPos, new Rect(0, 0, lastRect.width - 15, totalHeight));
            UpdateCheckingTextureResultsShowItem(lastRect, resultScrollPos);
            GUI.EndScrollView();
        }

        // 优化节点加载，只加载显示出来的部分
        private void UpdateCheckingTextureResultsShowItem(Rect rect, Vector2 resultScrollPos)
        {
            int startIndex = -1;
            float curHeight = 0; // 当前节点放置的高度
            float showRectEnd = resultScrollPos.y + rect.height; // 滚动容器内可视范围底部位置
            for (int k = 0; k < checkTexList.Count; k++)
            {
                float itemHeight = CalCheckingTextureResultsItemHeight(k);
                // 判断当前节点的高度是否在可视范围内
                bool show = curHeight <= showRectEnd && (curHeight + itemHeight > resultScrollPos.y);
                if (show)
                {
                    startIndex = startIndex == -1 ? k : startIndex;
                    DrawCheckingTextureAndCacheResultsItem(rect, curHeight, itemHeight, k);
                }
                else
                {
                    if (startIndex != -1) break;
                }

                curHeight += itemHeight + 5;
            }
        }
        
        private float CalCheckingTextureResultsItemHeight(int index)
        {
            int doubleCount = totalDoubleMatchedSimilarTextures[index].Count;
            int singleCount = totalSingleMatchedSimilarTextures[index].Count;
            float h = previewTexSize > 100 ? previewTexSize : 100;
            h += 5; // space
            if ((showDoubleMatch ? doubleCount : 0) + (showSingleMatch ? singleCount : 0) <= 0)
            {
                h += 22;
            }
            else
            {
                if (showDoubleMatch && doubleCount > 0)
                {
                    h += 22 + previewTexSize + 18;
                }

                if (showSingleMatch && singleCount > 0)
                {
                    h += 22 + previewTexSize + 18;
                }
            }
            h += 5; // space
            return h;
        }
        
        private void DrawCheckingTextureAndCacheResultsItem(Rect rect, float height, float itemHeight, int k)
        {
            int doubleCount = totalDoubleMatchedSimilarTextures[k].Count;
            int singleCount = totalSingleMatchedSimilarTextures[k].Count;

            Texture2D curTex = checkTexList[k]; // 当前查找重复贴图的贴图
            string curTexPath = checkTexPathList[k];
            bool isDeleted = checkTexDeleteMarkList[k];
            GUI.Box(new Rect(0, height, rect.width, itemHeight), GUIContent.none, EditorStyles.helpBox);
            // 当前查看的图片的信息，需要详实地罗列出来
            Rect btnRect = new Rect(5, height, previewTexSize, previewTexSize);
            if (!isDeleted && GUI.Button(btnRect, curTex))
            {
                EditorGUIUtility.PingObject(curTex); 
            }
            float detailHeight = 16;
            float detailSpace = 20;
            float h = previewTexSize > 100 ? previewTexSize : 100;
            
            // 图片信息在这边补充
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), curTexPath);
            FileInfo fileInfo = !isDeleted ? new FileInfo(fullPath) : null;
            int showMatchNum = (showDoubleMatch ? doubleCount : 0) + (showSingleMatch ? singleCount : 0);
            int contentNum = 4;
            contentNum += showMatchNum > 0 ? 1 : 0;
            float contentYOffset = (h - detailSpace * contentNum) / 2 ;
            
            GUI.Label(new Rect(previewTexSize + 10, height + contentYOffset, rect.width - previewTexSize, detailHeight),
                string.Format("搜索图片名称：{0}", curTex.name));
            GUI.Label(new Rect(previewTexSize + 10, height + detailSpace + contentYOffset, rect.width - previewTexSize, detailHeight),
                string.Format("搜索图片所在路径：{0}", curTexPath));
            GUI.Label(new Rect(previewTexSize + 10, height + detailSpace * 2 + contentYOffset, rect.width - previewTexSize, detailHeight),
                string.Format("搜索图片尺寸：{0}x{1}", curTex.width, curTex.height));
            if (!isDeleted && fileInfo != null)
            {
                GUI.Label(new Rect(previewTexSize + 10, height + detailSpace * 3 + contentYOffset, rect.width - previewTexSize, detailHeight),
                    string.Format("搜索图片大小：{0} KB", (fileInfo.Length / 1024d).ToString("F")));
            }
            else
            {
                GUI.Label(new Rect(previewTexSize + 10, height + detailSpace * 3 + contentYOffset, rect.width - previewTexSize, detailHeight),
                    "<color=#ff0000>搜索图片已被删除！</color>");
            }
            if (GUI.Button(new Rect(previewTexSize + 10, height + detailSpace * 4 + contentYOffset, 200, detailHeight), 
                    "打开批量操作面板"))
            {
                OnClickBatchModifyButton(k, curTexPath, totalDoubleMatchedSimilarTextures[k], totalSingleMatchedSimilarTextures[k]);
            }
            
            h += height + 5f;
            if (showMatchNum <= 0)
            {
                GUI.Label(new Rect(5, h, rect.width, 22), 
                    "无满足匹配条件的相似图片", doubleMatchTitleStyle);
            }
            else
            {
                if (showDoubleMatch && totalDoubleMatchedSimilarTextures[k].Count > 0)
                {
                    checkResultScrollRectPos[2 * k] = DrawCacheResultData(rect, h, k, true, 
                        isDeleted, totalDoubleMatchedSimilarTextures[k],
                        checkResultScrollRectPos[2 * k], curTexPath);
                    h += previewTexSize + 18 + 22;
                }

                if (showSingleMatch && totalSingleMatchedSimilarTextures[k].Count > 0)
                {
                    checkResultScrollRectPos[2 * k + 1] = DrawCacheResultData(rect, h, k, false, 
                        isDeleted, totalSingleMatchedSimilarTextures[k],
                        checkResultScrollRectPos[2 * k + 1], curTexPath);
                }
            }
        }
        
        // 绘制结果数据列表
        private Vector2 DrawCacheResultData(Rect rect, float height, int index, bool isDoubleMatch, bool isDeleted,
            List<SimilarTextureCacheData> cacheDataList, Vector2 scrollRectPos, string curTexPath)
        {
            string str = isDoubleMatch ? SimilarTextureCheckToolUtil.DoubleMatchTitle : SimilarTextureCheckToolUtil.SingleMatchTitle;
            
            GUI.Label(new Rect(5, height, rect.width, 22),
                str, isDoubleMatch ? doubleMatchTitleStyle : singleMatchTitleStyle);
            height += 22;
            
            scrollRectPos = GUI.BeginScrollView(
                new Rect(0, height, rect.width, previewTexSize + 18),
                scrollRectPos, 
                new Rect(0, 0, cacheDataList.Count * previewTexSize, previewTexSize));
            
            // 按需加载
            int startIndex = -1;
            float showRectEnd = scrollRectPos.x + rect.width;
            for (int i = 0; i < cacheDataList.Count; i ++)
            {
                float imgPosX = i * previewTexSize + 5;
                bool show = imgPosX <= showRectEnd && (imgPosX + previewTexSize > scrollRectPos.x);
                if (show)
                {
                    startIndex = startIndex == -1 ? i : startIndex;
                    SimilarTextureCacheData cacheData = cacheDataList[i];
                    Texture2D tempTex = AssetDatabase.LoadAssetAtPath<Texture2D>(cacheData.texturePath);
                    Rect btnRect = new Rect(imgPosX, 9, previewTexSize, previewTexSize);
                    if (tempTex != null && !isDeleted && (GUI.Button(btnRect, tempTex)))
                    {
                        OnClickResultTexture(index, curTexPath, cacheData, isDoubleMatch);
                    }
                }
                else
                {
                    if (startIndex != -1) break;
                }
                
            }
            GUI.EndScrollView();

            return scrollRectPos;
        }
        #endregion

        #region 界面计算

        /// <summary>
        /// 扫描项目所有图片资源，按需生成对比数据hash
        /// </summary>
        private void CheckALLTextureResourcesAndCacheHashAndHistogramData()
        {
            // 获取所有图片文件的路径
            allAssetPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/")).ToArray();
            updateIndex = 0;
            updateTotalNum = allAssetPaths.Length;
            texturePaths.Clear();
            EditorApplication.update += GetTexturePathDelegate;
        }

        private void LoadModifyPathsSaveCacheAndUpdateData()
        {
            if (File.Exists(SimilarTextureCheckToolUtil.ModifyPathsSavePath))
            {
                //反序列化数据
                FileStream fs = File.OpenRead(SimilarTextureCheckToolUtil.ModifyPathsSavePath);
                try
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    EditorUtility.DisplayCancelableProgressBar("正在导入缓存修改记录", "读取中...", 0);
                    List<string> cacheImportedList = (List<string>) bf.Deserialize(fs);
                    List<string> cacheDeletedList = (List<string>) bf.Deserialize(fs);
                    List<string> cacheMovedList = (List<string>) bf.Deserialize(fs);
                    List<string> cacheMovedFromList = (List<string>) bf.Deserialize(fs);
                    EditorUtility.ClearProgressBar();
                    UpdateTextureResourcesAndCacheHashAndHistogramData(cacheImportedList, cacheDeletedList, cacheMovedList, cacheMovedFromList);
                }
                finally
                {
                    fs.Close();
                }
                // 完成更新后，删除这个缓存文件
                File.Delete(SimilarTextureCheckToolUtil.ModifyPathsSavePath);
            }
        }

        private void UpdateTextureResourcesAndCacheHashAndHistogramData(
            List<string> importedList,
            List<string> deletedList,
            List<string> movedList,
            List<string> movedFromList)
        {
            bool needSaveAsset = false;
            bool needCheckAtlas = !string.Empty.Equals(atlasTextureFolderPath) && !string.Empty.Equals(sourceTextureFolderPath);
            // 删除，直接清掉已有缓存
            // 这个最好先于导入和移动处理，但需要判断是否是图集，图集在对比的时候是溯源的，因此图集不处理
            if (deletedList.Count > 0)
            {
                foreach (var texPath in deletedList)
                {
                    if(needCheckAtlas && texPath.Contains(atlasTextureFolderPath)) // 跳过图集
                        continue;
                    cacheSimilarData.TryRemoveCacheData(texPath);
                    needSaveAsset = true;
                }
            }
            
            // 移动，尽量不重新计算，而是移动键值
            // 同样，对于图集的文件不进行处理
            if (movedList.Count > 0 && movedFromList.Count > 0 && movedList.Count == movedFromList.Count)
            {
                mergeMovedPathsMap.Clear();
                foreach (var oldPath in movedFromList)
                {
                    if(needCheckAtlas && oldPath.Contains(atlasTextureFolderPath)) // 跳过图集
                        continue;
                    string fileName = Path.GetFileName(oldPath);
                    if(!string.Empty.Equals(fileName))
                        mergeMovedPathsMap.Add(fileName, oldPath);
                }
                // 新文件可能是改了名的，因此这里还是需要有一个创建缓存的保底机制 
                foreach (var newPath in movedList)
                {
                    string fileName = Path.GetFileName(newPath);
                    if(needCheckAtlas && newPath.Contains(atlasTextureFolderPath)) // 跳过图集
                        continue;
                    // 单纯的移动，能找到被移动前的缓存，就更换缓存即可
                    if (mergeMovedPathsMap.ContainsKey(fileName))
                    {
                        string oldPath = mergeMovedPathsMap[fileName];
                        SimilarTextureCacheData inst = cacheSimilarData.TryGetCacheData(oldPath);
                        if (inst != null)
                        {
                            inst.texturePath = newPath;
                        }
                        else
                        {
                            // 重新创建，放到导入列表就行
                            importedList.Add(newPath);
                        }
                        cacheSimilarData.TryRemoveCacheData(oldPath);
                        mergeMovedPathsMap.Remove(fileName);
                        if (inst != null)
                        {
                            cacheSimilarData.AppendCacheData(inst);
                            needSaveAsset = true;
                        }
                    }
                    else
                    {
                        // 重新创建，放到导入列表就行
                        importedList.Add(newPath);
                    }
                }
                // 找到没有成功更换的缓存，直接删除
                if (mergeMovedPathsMap.Count > 0)
                {
                    foreach (var kv in mergeMovedPathsMap)
                    {
                        cacheSimilarData.TryRemoveCacheData(kv.Value);
                        needSaveAsset = true;
                    }
                }
            }
            
            // 新增导入，直接创建插入
            if (importedList.Count > 0)
            {
                texturePaths.Clear();
                foreach (var texPath in importedList)
                {
                    if(needCheckAtlas && texPath.Contains(atlasTextureFolderPath)) // 跳过图集
                        continue;
                    texturePaths.Add(texPath);
                }
                updateTotalNum = texturePaths.Count;
                if (updateTotalNum > 0)
                {
                    updateIndex = 0;
                    needSaveAsset = false; // ProcessTextureDelegate里面会保存
                    tempCacheSimilarDict.Clear();
                    EditorApplication.update += ProcessTextureDelegate;
                }
            }
            // 没有新增，则在这里调度保存
            if (needSaveAsset)
            {
                SaveCacheData();
            }
        }

        /// <summary>
        /// 过滤搜索到的全部资产路径，只留下图片资产路径
        /// </summary>
        private void GetTexturePathDelegate()
        {
            string path = allAssetPaths[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(string.Format("正在过滤图片路径({0}/{1})", updateIndex, updateTotalNum),
                path, (float)updateIndex / updateTotalNum);
            
            foreach (var extension in SimilarTextureCheckToolUtil.TextureExtensions)
            {
                if (path.EndsWith(extension))
                {
                    texturePaths.Add(path);
                    break;
                }
            }
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 过滤完成
            {
                EditorApplication.update -= GetTexturePathDelegate;
                updateIndex = 0;
                updateTotalNum = texturePaths.Count;
                if (updateTotalNum > 0)
                {
                    tempCacheSimilarDict.Clear();
                    EditorApplication.update += ProcessTextureDelegate;
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "项目中没有图片资源可以处理" , "Ok");
                }
            }
            if (isCancel)
            {
                EditorApplication.update -= GetTexturePathDelegate;
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
            }
        }

        /// <summary>
        /// 预处理获取到的图片路径下的图片资产特征值
        /// </summary>
        private void ProcessTextureDelegate()
        {
            string texPath = texturePaths[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(string.Format("正在预计算图片特征值({0}/{1})", updateIndex, updateTotalNum), texPath, (float)updateIndex / updateTotalNum);
            if (!CanSkipTextureCalculation(texPath))
            {
                var inst = SimilarTextureCheckToolUtil.CreateNewCacheData(texPath);
                if (inst != null)
                    tempCacheSimilarDict.Add(texPath, inst);
            }

            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                EditorApplication.update -= ProcessTextureDelegate;
                updateIndex = 0;
                SaveCacheData();
                EditorUtility.ClearProgressBar();
            }
            if (isCancel)
            {
                EditorApplication.update -= ProcessTextureDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                SaveCacheData();
                EditorUtility.ClearProgressBar();
            }
        }

        // 保存预计算图片特征值缓存
        public void SaveCacheData()
        {
            if (cacheSimilarData == null)
            {
                cacheSimilarData = CreateInstance<SimilarTextureCacheAsset>();
                cacheSimilarData.AppendCacheDataFormTempDic(tempCacheSimilarDict);
            }
            else
            {
                cacheSimilarData.AppendCacheDataFormTempDic(tempCacheSimilarDict);
            }
            if (File.Exists(SimilarTextureCheckToolUtil.CacheDataSavePath))
                File.Delete(SimilarTextureCheckToolUtil.CacheDataSavePath);
            using (FileStream fs = File.OpenWrite(SimilarTextureCheckToolUtil.CacheDataSavePath))
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(fs, cacheSimilarData.savedCacheDataList);
            }
            tempCacheSimilarDict.Clear();
        }

        /// <summary>
        /// 处理单张图片的相似图片查找
        /// </summary>
        private void TryGetTargetTextureSimilarTextures()
        {
            ClearAllCacheData();
            if (!IsComparisonEnabled())
            {
                return;
            }
            
            // 将计算单张图片相似纹理的行为看作是只有一张贴图的列表的情况
            string path = AssetDatabase.GetAssetPath(curCheckTex);
            SimilarTextureCheckToolUtil.AppendTextureToCheckTextureList(curCheckTex, path, ref checkTexList, ref checkTexPathList);
            // 初始化删除标记列表
            for (int i = 0; i < checkTexList.Count; i++)
            {
                checkTexDeleteMarkList.Add(false);
            }
            
            StartComparisonMainProcessing();
        }

        
        /// <summary>
        /// 处理对目标文件夹内所有图片的相似图片查找
        /// </summary>
        private void TryGetTargetFolderSimilarTextures()
        {
            ClearAllCacheData();
            if (!IsComparisonEnabled())
            {
                return;
            }
            
            // 计算出绝对路径后，搜索文件夹内的图片资源的绝对路径
            string path = AssetDatabase.GetAssetPath(curFolderAsset);
            string sysPath = path.Replace("Assets", Application.dataPath);
            var textureFileInfos = Directory.GetFiles(sysPath, "*.*", SearchOption.AllDirectories)
                .Where(s => SimilarTextureCheckToolUtil.TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
            
            foreach (var textureSysPath in textureFileInfos)
            {
                string texturePath = textureSysPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                // Debug.LogFormat("texturePath = {0}", texturePath);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture != null)
                {
                    SimilarTextureCheckToolUtil.AppendTextureToCheckTextureList(texture, texturePath, ref checkTexList, ref checkTexPathList);
                }
            }

            if (checkTexList.Count <= 0)
            {
                Debug.LogFormat("当前文件夹下没有图片资源");
                return;
            }
            // 初始化删除标记列表
            for (int i = 0; i < checkTexList.Count; i++)
            {
                checkTexDeleteMarkList.Add(false);
            }
            StartComparisonMainProcessing();
        }
        
        /// <summary>
        /// 判断是否可以执行对图片资源的收集和比对
        /// </summary>
        /// <returns></returns>
        private bool IsComparisonEnabled()
        {
            // 设置了限制对比文件夹的话，当限制文件夹内存在贴图则可以进行比对
            if (!string.Empty.Equals(limitCompareTextureFolderPath) && limitCompareTextureFolderAsset != null)
            {
                CalculateLimitFolderTexturesPath();
                if (limitFolderTexList.Count <= 0)
                {
                    Debug.LogError("限制对比的文件夹内没有图片");
                    return false;
                }
                return true;
            }
            // 反之，检查是否有现有的缓存数据
            if (cacheSimilarData == null || cacheSimilarData.savedCacheDataList.Count() <= 0)
            {
                Debug.LogError("当前无缓存数据，请重新生成");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 先获取到目标限制目录下的图片资源和路径
        /// </summary>
        private void CalculateLimitFolderTexturesPath()
        {
            if (string.Empty.Equals(limitCompareTextureFolderPath) || limitCompareTextureFolderAsset == null)
            {
                return;
            }
            
            string sysPath = limitCompareTextureFolderPath.Replace("Assets", Application.dataPath);
            var textureFileInfos = Directory.GetFiles(sysPath, "*.*", SearchOption.AllDirectories)
                .Where(s => SimilarTextureCheckToolUtil.TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
            
            foreach (var textureSysPath in textureFileInfos)
            {
                string texturePath = textureSysPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture != null)
                {
                    SimilarTextureCheckToolUtil.AppendTextureToCheckTextureList(texture, texturePath, ref limitFolderTexList, ref limitFolderTexPathList);
                }
            }
        }

        /// <summary>
        /// 数据准备完成后的主要逻辑入口
        /// 当存在限制对比文件夹时，会更新对比文件夹内的所有图片资源的图片特征信息再进行对目标图片信息的比对
        /// 反之，根据已有缓存，与目标图片信息进行比对
        /// </summary>
        private void StartComparisonMainProcessing()
        {
            CollectingCacheData = true;
            if (cacheSimilarData == null)
            {
                cacheSimilarData = CreateInstance<SimilarTextureCacheAsset>();
            }
            // 这个列表每次调度比对前都会初始化和填入数据，因此如果该列表有数据则代表是限制了文件夹比对
            if (limitFolderTexList.Count > 0)
            {
                StartCheckLimitFolderTextureListCacheDataDelegate();
            }
            else
            {
                StartCheckTargetTextureListCacheDataDelegate();
            }
        }
        
        /// <summary>
        /// 开始预计算限制对比文件夹内的图片的特征值信息
        /// </summary>
        private void StartCheckLimitFolderTextureListCacheDataDelegate()
        {
            updateIndex = 0;
            updateTotalNum = limitFolderTexList.Count;
            // 先检查这批文件夹里面的图片是否已经有缓存特征值了
            EditorApplication.update += CheckLimitFolderTextureListCacheDataDelegate;
        }
        
        /// <summary>
        /// 开始预计算目标文件的图片特征值
        /// </summary>
        private void StartCheckTargetTextureListCacheDataDelegate()
        {
            updateIndex = 0;
            updateTotalNum = checkTexList.Count;
            // 先检查这批文件夹里面的图片是否已经有缓存特征值了
            EditorApplication.update += CheckTargetTextureListCacheDataDelegate;
        }

        /// <summary>
        /// 预计算限制对比文件夹内的图片的特征值
        /// </summary>
        private void CheckLimitFolderTextureListCacheDataDelegate()
        {
            string path = limitFolderTexPathList[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(string.Format("检查文件夹内的图片特征值({0}/{1})", updateIndex, updateTotalNum),
                path, (float)updateIndex / updateTotalNum);
            SimilarTextureCacheData cacheData = cacheSimilarData.TryGetCacheData(path);
            if (cacheData == null)
            {
                cacheData = SimilarTextureCheckToolUtil.CreateNewCacheData(path);
                if(cacheData != null)
                    tempCacheSimilarDict.Add(path, cacheData);
            }
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                EditorApplication.update -= CheckLimitFolderTextureListCacheDataDelegate;
                SaveCacheData();

                CollectingCacheData = true;
                // 进行下一步，预计算目标图片路径下的图片特征值信息
                StartCheckTargetTextureListCacheDataDelegate();
            }
            if (isCancel)
            {
                EditorApplication.update -= CheckLimitFolderTextureListCacheDataDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                SaveCacheData(); 
                updateIndex = 0;
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// 预计算目标图片的特征值
        /// </summary>
        private void CheckTargetTextureListCacheDataDelegate()
        {
            string path = checkTexPathList[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(string.Format("检查文件夹内的图片特征值({0}/{1})", updateIndex, updateTotalNum),
                path, (float)updateIndex / updateTotalNum);
            SimilarTextureCacheData cacheData = cacheSimilarData.TryGetCacheData(path);
            if (cacheData == null)
            {
                cacheData = SimilarTextureCheckToolUtil.CreateNewCacheData(path, curCheckTex);
                if(cacheData != null)
                    tempCacheSimilarDict.Add(path, cacheData);
            }
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                EditorApplication.update -= CheckTargetTextureListCacheDataDelegate;
                SaveCacheData(); 
                // 开始调度对这批文件夹的图片的遍历操作
                updateIndex = 0;
                checkTexListIndex = 0;
                // 初始化满足列表数据
                totalDoubleMatchedSimilarTextures.Clear();
                totalSingleMatchedSimilarTextures.Clear();
                P_TotalMatchedSimilarTextures.Clear();
                H_TotalMatchedSimilarTextures.Clear();
                for (int i = 0; i < checkTexPathList.Count; i++)
                {
                    totalDoubleMatchedSimilarTextures.Add(new List<SimilarTextureCacheData>());
                    totalSingleMatchedSimilarTextures.Add(new List<SimilarTextureCacheData>());
                    P_TotalMatchedSimilarTextures.Add(new List<SimilarTextureCacheData>());
                    H_TotalMatchedSimilarTextures.Add(new List<SimilarTextureCacheData>());
                    // 一次需要添加两个位置
                    checkResultScrollRectPos.Add(Vector2.zero);
                    checkResultScrollRectPos.Add(Vector2.zero);
                }
                
                CollectingCacheData = true;
                if (limitFolderTexPathList.Count > 0)
                {
                    updateTotalNum = limitFolderTexPathList.Count;
                    EditorApplication.update += GetTargetTextureListSimilarTexturesInLimitFolderDelegate;
                }
                else
                {
                    updateTotalNum = cacheSimilarData.savedCacheDataList.Count;
                    EditorApplication.update += GetTargetTextureListSimilarTexturesDelegate;
                }
            }
            if (isCancel)
            {
                EditorApplication.update -= CheckTargetTextureListCacheDataDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                SaveCacheData(); 
                updateIndex = 0;
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// 获取与目标图片的相似图片
        /// </summary>
        private void GetTargetTextureListSimilarTexturesDelegate()
        {
            string curTextPath = checkTexPathList[checkTexListIndex];
            var curCacheData = cacheSimilarData.TryGetCacheData(curTextPath);

            SimilarTextureCacheData cacheData = cacheSimilarData.savedCacheDataList[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(
                string.Format("正在比对图片特征值({0}/{1}) : {2}/{3}", checkTexListIndex, checkTexPathList.Count, updateIndex, updateTotalNum),
                cacheData.texturePath, (float)updateIndex / updateTotalNum);

            // 需要排除自己
            if (!cacheData.texturePath.Equals(curCacheData.texturePath))
            {
                int perceptualCounter = SimilarTextureCheckToolUtil.PerceptualDistance(curCacheData.perceptualHash, cacheData.perceptualHash);
                double histogramSimilarity = SimilarTextureCheckToolUtil.CalcSimilarity(curCacheData.ImageHistogramData, cacheData.ImageHistogramData);

                bool P_Match = perceptualCounter < PerceptualHashThreshold;
                bool H_Match = histogramSimilarity >= ImageHistogramThreshold;

                // 分别存储数据
                if (P_Match && H_Match)
                {
                    totalDoubleMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                }
                else
                {
                    if(P_Match)
                    {
                        P_TotalMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                    }
                    if(H_Match)
                    {
                        H_TotalMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                    }
                }
            }
            
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                updateIndex = 0;
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(P_TotalMatchedSimilarTextures[checkTexListIndex]);
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(H_TotalMatchedSimilarTextures[checkTexListIndex]);
                checkTexListIndex++;
                if (checkTexListIndex >= checkTexPathList.Count) // 全部结束，该更新window了
                {
                    EditorApplication.update -= GetTargetTextureListSimilarTexturesDelegate;
                    CollectingCacheData = false;
                    EditorUtility.ClearProgressBar();
                    Repaint();
                }
            }
            if (isCancel)
            {
                EditorApplication.update -= GetTargetTextureListSimilarTexturesDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(P_TotalMatchedSimilarTextures[checkTexListIndex]);
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(H_TotalMatchedSimilarTextures[checkTexListIndex]);
                updateIndex = 0;
                CollectingCacheData = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }
        
        /// <summary>
        /// 获取与目标图片的相似图片
        /// </summary>
        private void GetTargetTextureListSimilarTexturesInLimitFolderDelegate()
        {
            string curTextPath = checkTexPathList[checkTexListIndex];
            var curCacheData = cacheSimilarData.TryGetCacheData(curTextPath);
            string limitTexPath = limitFolderTexPathList[updateIndex];
            SimilarTextureCacheData cacheData = cacheSimilarData.TryGetCacheData(limitTexPath);
            if (cacheData == null)
            {
                updateIndex++;
                CheckGC();
                return;
            }
            
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(
                string.Format("正在比对图片特征值({0}/{1} ：{2}/{3})", checkTexListIndex, checkTexPathList.Count, updateIndex, updateTotalNum),
                cacheData.texturePath, (float)updateIndex / updateTotalNum);

            // 需要排除自己
            if (!cacheData.texturePath.Equals(curCacheData.texturePath))
            {
                int perceptualCounter = SimilarTextureCheckToolUtil.PerceptualDistance(curCacheData.perceptualHash, cacheData.perceptualHash);
                double histogramSimilarity = SimilarTextureCheckToolUtil.CalcSimilarity(curCacheData.ImageHistogramData, cacheData.ImageHistogramData);

                bool P_Match = perceptualCounter < PerceptualHashThreshold;
                bool H_Match = histogramSimilarity >= ImageHistogramThreshold;

                // 分别存储数据
                if (P_Match && H_Match)
                {
                    totalDoubleMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                }
                else
                {
                    if(P_Match)
                    {
                        P_TotalMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                    }
                    if(H_Match)
                    {
                        H_TotalMatchedSimilarTextures[checkTexListIndex].Add(cacheData);
                    }
                }
            }
            
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                updateIndex = 0;
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(P_TotalMatchedSimilarTextures[checkTexListIndex]);
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(H_TotalMatchedSimilarTextures[checkTexListIndex]);
                checkTexListIndex++;
                if (checkTexListIndex >= checkTexPathList.Count) // 全部结束，该更新window了
                {
                    EditorApplication.update -= GetTargetTextureListSimilarTexturesInLimitFolderDelegate;
                    CollectingCacheData = false;
                    EditorUtility.ClearProgressBar();
                    Repaint();
                }
            }
            if (isCancel)
            {
                EditorApplication.update -= GetTargetTextureListSimilarTexturesInLimitFolderDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(P_TotalMatchedSimilarTextures[checkTexListIndex]);
                totalSingleMatchedSimilarTextures[checkTexListIndex].AddRange(H_TotalMatchedSimilarTextures[checkTexListIndex]);
                updateIndex = 0;
                CollectingCacheData = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        /// <summary>
        /// 清空缓存和操作面板
        /// </summary>
        private void ClearAllCacheData()
        {
            limitFolderTexList.Clear();
            limitFolderTexPathList.Clear();
            checkTexList.Clear();
            checkTexPathList.Clear();
            checkTexDeleteMarkList.Clear();
            checkTexturesFoundResultList.Clear();
            checkResultScrollRectPos.Clear();
            resultScrollPos = Vector2.zero;
            RemoveAllBatchModifySubWindow();
        }

        private bool CanSkipTextureCalculation(Texture2D tex)
        {
            if (cacheSimilarData == null) return false;
            return cacheSimilarData.CanSkipTextureCalculation(tex);
        }
        private bool CanSkipTextureCalculation(string path)
        {
            if (cacheSimilarData == null) return false;
            return cacheSimilarData.CanSkipTextureCalculation(path);
        }

        private void CheckGC()
        {
            if (updateIndex % 2000 == 0)
                GC.Collect();
        }
        #endregion

        #region 操作界面（OperateWindow）相关逻辑

        /// <summary>
        /// 检测是否需要重新初始化资源引用数据
        /// </summary>
        public void InitReferenceFinderData()
        {
            if (!initedReferenceFinderData)
            {
                //初始化数据
                if(!referenceFinderData.ReadFromCache())
                {
                    referenceFinderData.CollectDependenciesInfo();
                }
                initedReferenceFinderData = true;
            }
        }

        public void CollectDependenciesInfo()
        {
            referenceFinderData.CollectDependenciesInfo();
        }

        public bool IsReferenceCollectFinished()
        {
            return referenceFinderData.collectFinished;
        }
        
        public AssetDescription GetReferenceData(string guid)
        {
            // 先判断是否有对应缓存，否则就需要触发全局的引用遍历
            if (!referenceFinderData.assetDict.ContainsKey(guid))
                referenceFinderData.CollectDependenciesInfo();
            
            return referenceFinderData.assetDict.ContainsKey(guid) ? referenceFinderData.assetDict[guid] : null;
        }

        public void UpdateAssetState(string guid)
        {
            referenceFinderData.UpdateAssetState(guid);
        }
        
        /// <summary>
        /// 获取图片的匹配结果
        /// </summary>
        /// <param name="sourceTexturePath">源图</param>
        /// <param name="matchedTexturePath">匹配结果图</param>
        /// <param name="p_Match">感知哈希值匹配结果</param>
        /// <param name="h_Match">直方图匹配结果</param>
        public void GetTextureMatchStatus(string sourceTexturePath, string matchedTexturePath, out bool p_Match, out bool h_Match)
        {
            p_Match = false;
            h_Match = false;
            int listIndex = 0;
            foreach (var path in checkTexPathList)
            {
                if (sourceTexturePath.Equals(path)) break;
                listIndex++;
            }
            if (listIndex >= checkTexPathList.Count) return;

            var cacheData = cacheSimilarData.TryGetCacheData(matchedTexturePath);
            if (cacheData != null)
            {
                bool isInDoubleMatchList = totalDoubleMatchedSimilarTextures[listIndex].Contains(cacheData);
                if (isInDoubleMatchList)
                {
                    p_Match = true;
                    h_Match = true;
                }
                else
                {
                    p_Match = P_TotalMatchedSimilarTextures[listIndex].Contains(cacheData);
                    h_Match = H_TotalMatchedSimilarTextures[listIndex].Contains(cacheData);
                }
            }
        }

        /// <summary>
        /// 操作面板删除数据后，这边同步删除收集的界面缓存
        /// </summary>
        /// <param name="index"></param>
        /// <param name="deleteResPath"></param>
        public void UpdateBatchDataAfterDeleteRes(int index, Dictionary<string,bool> deleteResPath)
        {
            if (checkTexList.Count + 1 < index) return;
            int doubleCount = totalDoubleMatchedSimilarTextures[index].Count;
            if (doubleCount > 0)
            {
                for (int k = doubleCount - 1; k >= 0; k--)
                {
                    SimilarTextureCacheData cacheData = totalDoubleMatchedSimilarTextures[index][k];
                    if (deleteResPath.ContainsKey(cacheData.texturePath))
                    {
                        totalDoubleMatchedSimilarTextures[index].RemoveAt(k);
                    }
                }
            }
            int singleCount = totalSingleMatchedSimilarTextures[index].Count;
            if (singleCount > 0)
            {
                for (int k = singleCount - 1; k >= 0; k--)
                {
                    SimilarTextureCacheData cacheData = totalSingleMatchedSimilarTextures[index][k];
                    if (deleteResPath.ContainsKey(cacheData.texturePath))
                    {
                        totalSingleMatchedSimilarTextures[index].RemoveAt(k);
                    }
                }
            }
        }

        /// <summary>
        /// 将目标搜索对象标记为已被删除
        /// </summary>
        /// <param name="index"></param>
        public void MarkDeletedSearchItem(int index)
        {
            if(checkTexDeleteMarkList.Count - 1 >= index)
                checkTexDeleteMarkList[index] = true;
        }

        public bool GetSearchItemDeleteMark(int index)
        {
            if(checkTexDeleteMarkList.Count - 1 >= index)
                return checkTexDeleteMarkList[index];
            return false;
        }
        
        private void OnClickResultTexture(int index, string searchTexPath, SimilarTextureCacheData matchCacheData, bool isDoubleMatch)
        {
            SimilarTextureCacheData searchTexCacheData = cacheSimilarData.TryGetCacheData(searchTexPath);
            List<SimilarTextureCacheData> doubleMatchCacheDataList = new List<SimilarTextureCacheData>();
            List<SimilarTextureCacheData> singleMatchCacheDataList = new List<SimilarTextureCacheData>();
            if (isDoubleMatch)
            {
                doubleMatchCacheDataList.Add(matchCacheData);
            }
            else
            {
                singleMatchCacheDataList.Add(matchCacheData);
            }

            OnClickBatchModifyButton(index, searchTexPath, doubleMatchCacheDataList, singleMatchCacheDataList, true);
        }

        private void OnClickBatchModifyButton(int index, string searchTexPath, 
            List<SimilarTextureCacheData> doubleMatchCacheDataList, 
            List<SimilarTextureCacheData> singleMatchCacheDataList, bool isSingleTexture = false)
        {
            SimilarTextureCacheData searchTexCacheData = cacheSimilarData.TryGetCacheData(searchTexPath);
            var subWindow = SimilarTextureBatchModifyWindow.OpenWindow(index, this, searchTexCacheData, 
                doubleMatchCacheDataList, singleMatchCacheDataList, isSingleTexture);
            if (subWindow != null)
            {
                batchModifySubWindowList.Add(subWindow);
            }
        }

        public void RemoveBatchModitySubWindow(SimilarTextureBatchModifyWindow window)
        {
            batchModifySubWindowList.Remove(window);
        }

        private void RemoveAllBatchModifySubWindow()
        {
            if (batchModifySubWindowList.Count <= 0) return;
            for (int i = 0; i < batchModifySubWindowList.Count; i++)
            {
                batchModifySubWindowList[i].Close();
            }
            batchModifySubWindowList.Clear();
        }
        
        private void OnDisable()
        {
            ClearAllCacheData();
            cacheSimilarData = null;
            GC.Collect();
        }
        #endregion
    }
}
