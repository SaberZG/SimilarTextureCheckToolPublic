using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.VersionControl;
using UnityEngine;
using Object = System.Object;

namespace SimilarTextureCheckTool
{
    public class SimilarTextureBatchModifyWindow : EditorWindow
    {
        public class BatchData
        {
            // 基本数据
            public Texture2D Tex = null;
            public string TexGUID = String.Empty;
            public string TexPath = String.Empty;
            public FileInfo TexFileInfo = null;
            // 可能存在的图集相关
            public Texture2D AtlasTex = null;
            public string AtlasTexGUID = String.Empty;
            public string AtlasTexPath = String.Empty;
            public string TexFileIdInAtlasTex = String.Empty;

            public BatchData(string texPath)
            {
                TexPath = texPath;
                Tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
                TexGUID = AssetDatabase.AssetPathToGUID(TexPath);
                string searchTexFullPath = Path.Combine(Directory.GetCurrentDirectory(), TexPath);
                TexFileInfo = new FileInfo(searchTexFullPath);
                
                // 尝试添加对比图对应的图集信息
                SimilarTextureCheckToolUtil.TryGetSpriteFileIdInAtlasTextureBySourceTexture(Tex,
                    out AtlasTex, out TexFileIdInAtlasTex);
                if (!string.Empty.Equals(TexFileIdInAtlasTex))
                {
                    AtlasTexPath = AssetDatabase.GetAssetPath(AtlasTex);
                    AtlasTexGUID = AssetDatabase.AssetPathToGUID(AtlasTexPath);
                }
            }
        }
        /// <summary>
        /// 打开相似贴图批量操作面板
        /// </summary>
        /// <param name="parent">父界面，用于对齐数据</param>
        /// <param name="searchTex">父界面查找相似贴图的贴图</param>
        /// <param name="doubleMatchCacheDataList">被判定与<paramref name="searchTex">searchTex</paramref>基本相似的纹理缓存数据</param>
        /// <param name="singleMatchCacheDataList">被判定与<paramref name="searchTex">searchTex</paramref>可能相似的纹理缓存数据</param>
        public static SimilarTextureBatchModifyWindow OpenWindow(
            int index, 
            SimilarTextureCheckWindow parent, 
            SimilarTextureCacheData searchTexCacheData, 
            List<SimilarTextureCacheData> doubleMatchCacheDataList, 
            List<SimilarTextureCacheData> singleMatchCacheDataList,
            bool isSingleTexture = false)
        {
            if (parent == null || searchTexCacheData == null) return null;
            
            var window = CreateWindow<SimilarTextureBatchModifyWindow>();
            window.titleContent = new GUIContent("相似图片批量操作面板");
            window.minSize = new Vector2(800, 600);
            window.maxSize = new Vector2(1400, 1200);
            window.InitStyles();
            window.Init(index, parent, searchTexCacheData, doubleMatchCacheDataList, singleMatchCacheDataList, isSingleTexture);
            
            return window;
        }

        private bool inited = false;
        private bool referenceDataLoadFinished = false;
        private int index;
        private SimilarTextureCheckWindow parent;
        private List<SimilarTextureCacheData> doubleMatchCacheDataList = null;
        private List<SimilarTextureCacheData> singleMatchCacheDataList = null;
        private Dictionary<string, bool> deleteResPath = new Dictionary<string, bool>();

        #region 传入参数的预计算
        /// 传入的比对图数据集，若选择使用拷贝公共图集内的资源，此对象可能会被置空
        private BatchData searchTexBatchData = null;
        /// 传入对比图的图片特征缓存数据，打开面板时初始化一次，不会因searchTexBatchData的删除而删除，用于对比拷贝公共图集的图片特征值
        private SimilarTextureCacheData searchTexCacheData;
        /// 传入的满足匹配规则的数据集
        private List<BatchData> doubleMatchTexBatchDataList = new List<BatchData>();
        private List<BatchData> singleMatchTexBatchDataList = new List<BatchData>();
        /// 是否是查找界面中点击单个结果的操作，如果是，则不需要太复杂的treeView
        public bool isSingleTexture = false;
        #endregion

        #region 拷贝替换相关
        private DefaultAsset copyTargetFolder;
        // 拷贝后的资源相关
        private List<Texture2D> checkTexList = new List<Texture2D>();
        private List<string> checkTexPathList = new List<string>();

        private BatchData mainReplaceBatchData = null;
        // 当前选中的替换数据是否是拷贝文件夹的公共资产数据
        // 是的话 searchTexBatchData 也要作为被替换对象
        private bool replaceDataIsCopyData = false; 
        // 编辑器Update相关
        private int updateIndex = 0;
        private int updateTotalNum = 0;
        private bool CollectingCacheData = false;
        private List<BatchData> copyFolderDoubleMatchedBatchData = new List<BatchData>();
        #endregion

        #region 资产结构GUI

        private BatchAssetTreeView m_BatchAssetTreeView;
        [SerializeField]
        private TreeViewState m_TreeViewState;
        private bool needUpdateAssetTree = true;
        // private bool needUpdateState = false;
        //生成root相关
        private HashSet<string> updatedAssetSet = new HashSet<string>();
        #endregion

        #region 替换操作/回滚操作相关

        /// <summary>
        /// GUID资产替换状态字典 格式为 [GUID = [path资产相对路径 = 是否执行了替换]]
        /// </summary>
        private Dictionary<string, Dictionary<string, bool>> guidReplacedDict = new Dictionary<string, Dictionary<string, bool>>();
        /// <summary>
        /// 查找到的图集GUID资产替换状态字典 格式为 [GUID = [path资产相对路径 = 是否执行了替换]]
        /// </summary>
        private Dictionary<string, Dictionary<string, bool>> guidAtlasReplacedDict = new Dictionary<string, Dictionary<string, bool>>();

        #endregion
        
        #region styles
        private bool initedStyles = false;
        private GUIStyle headTitleStyle;
        private GUIStyle matchLabel;
        // 工具栏按钮样式
        private GUIStyle toolbarButtonGUIStyle;
        //工具栏样式
        private GUIStyle toolbarGUIStyle;
        #endregion
        
        private Rect treeViewRect
        {
            get { return  GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)); }
        }
        public void Init(
            int index, 
            SimilarTextureCheckWindow parent, 
            SimilarTextureCacheData searchTexCacheData, 
            List<SimilarTextureCacheData> doubleMatchCacheDataList, 
            List<SimilarTextureCacheData> singleMatchCacheDataList,
            bool isSingleTexture = false)
        {
            this.index = index;
            this.parent = parent;
            this.searchTexCacheData = searchTexCacheData;
            this.doubleMatchCacheDataList = doubleMatchCacheDataList;
            this.singleMatchCacheDataList = singleMatchCacheDataList;
            this.isSingleTexture = isSingleTexture;
            deleteResPath.Clear();
            
            inited = parent != null && searchTexCacheData != null && doubleMatchCacheDataList != null && singleMatchCacheDataList != null;
            // 这里就初始化一些数据，避免在GUI循环中持续获取
            if (inited)
            {
                // 如果资源已经被删除则不要再加载BatchData数据了
                bool isDelete = parent.GetSearchItemDeleteMark(index);
                if(!isDelete)
                    searchTexBatchData = new BatchData(searchTexCacheData.texturePath);

                RecollectBatchDatas();
                parent.InitReferenceFinderData();
                UpdateCurTextureReferences();
            }
        }

        private void InitStyles()
        {
            if (!initedStyles)
            {
                // 初始化style
                headTitleStyle = new GUIStyle("HeaderLabel");
                headTitleStyle.fontSize = 20;
                matchLabel = EditorStyles.label;
                matchLabel.richText = true;
                toolbarButtonGUIStyle = new GUIStyle("ToolbarButton");
                toolbarGUIStyle = new GUIStyle("Toolbar");
                initedStyles = true;
            }
        }
        
        private void RecollectBatchDatas()
        {
            doubleMatchTexBatchDataList.Clear();
            singleMatchTexBatchDataList.Clear();
            // 搜索的图片被删，则清除相关缓存
            if (searchTexBatchData != null && deleteResPath.ContainsKey(searchTexBatchData.TexPath))
            {
                searchTexBatchData = null;
            }
            // 检查基本相同的图片数据留存情况
            if (doubleMatchCacheDataList.Count > 0)
            {
                foreach (var cacheData in doubleMatchCacheDataList)
                {
                    if(!deleteResPath.ContainsKey(cacheData.texturePath))
                        doubleMatchTexBatchDataList.Add(new BatchData(cacheData.texturePath));
                }
            }
            // 检查可能相同的图片数据留存情况
            if (singleMatchCacheDataList.Count > 0)
            {
                foreach (var cacheData in singleMatchCacheDataList)
                {
                    if(!deleteResPath.ContainsKey(cacheData.texturePath))
                        singleMatchTexBatchDataList.Add(new BatchData(cacheData.texturePath));
                }
            }

        }

        #region 绘制逻辑
        private void OnGUI()
        {
            if (!inited) return;
            InitStyles();
            // 绘制搜索图片的信息
            if(searchTexBatchData != null) // 搜索数据没了就直接不生成这个东西了
                DrawTextureInfosAndReplaceBtn(searchTexBatchData, false, false);
            if (isSingleTexture)
            {
                if (doubleMatchTexBatchDataList.Count > 0)
                {
                    DrawTextureInfosAndReplaceBtn(doubleMatchTexBatchDataList[0], true, false);
                }
                if (singleMatchTexBatchDataList.Count > 0)
                {
                    DrawTextureInfosAndReplaceBtn(singleMatchTexBatchDataList[0], true, false);
                }
            }
            
            // 绘制拷贝图片文件夹的信息
            DrawCopyTextureTargetFolder();
            // 绘制拷贝图片文件夹中与搜索图片相似图片列表
            DrawSimilarTextureInCopyFolder();
            // 检查是否需要重新生成AssetTree数据
            UpdateAssetTree();
            // // 绘制引用参数信息
            DrawReferencesInfos();
        }

        private void DrawTextureInfosAndReplaceBtn(BatchData batchData, bool isSimilarData, bool isCopyData)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if(batchData != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(batchData.Tex, GUILayout.Width(parent.previewTexSize), GUILayout.Height(parent.previewTexSize)))
                {
                    EditorGUIUtility.PingObject(batchData.Tex);
                }
                EditorGUILayout.BeginVertical();

                string title1 = "搜索图片";
                title1 = isSimilarData ? "查看的相似图片" : title1;
                title1 = isCopyData ? "拷贝的公共图片" : title1;
                EditorGUILayout.LabelField(string.Format("{0}名称：{1}", title1, batchData.Tex.name));
                EditorGUILayout.LabelField(string.Format("图片所在路径：{0}", batchData.TexPath));
                EditorGUILayout.LabelField(string.Format("图片尺寸：{0}x{1}", batchData.Tex.width, batchData.Tex.height));
                if (batchData.TexFileInfo != null)
                {
                    EditorGUILayout.LabelField(string.Format("图片大小：{0} KB", (batchData.TexFileInfo.Length / 1024d).ToString("F")));
                }
                if (isSimilarData)
                {
                    parent.GetTextureMatchStatus(searchTexCacheData.texturePath, batchData.TexPath, out bool p_Match, out bool h_Match);
                    EditorGUILayout.LabelField(
                        string.Format("感知哈希值匹配判断：{0}", p_Match ? "<color=#00ff00>通过</color>" : "<color=#ff0000>不通过</color>"), 
                        matchLabel);
                    EditorGUILayout.LabelField(
                        string.Format("直方图匹配判断：{0}", h_Match ? "<color=#00ff00>通过</color>" : "<color=#ff0000>不通过</color>"), 
                        matchLabel);
                }
                EditorGUILayout.BeginHorizontal();

                if (!isSimilarData)
                {
                    GUI.enabled = mainReplaceBatchData != batchData;
                    if (GUILayout.Button("将这个图片作为替换用图片", GUILayout.MaxWidth(240), GUILayout.Height(20)))
                    {
                        SelectAsMainReplaceData(batchData, isCopyData);
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                GUI.enabled = searchTexBatchData != null;
                if (GUILayout.Button("复制搜索图片", GUILayout.Width(parent.previewTexSize), GUILayout.Height(40)))
                {
                    CopySearchTextureToCopyPath();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
        
        /// <summary>
        /// 绘制选取 拷贝至 目标文件夹GUI控件
        /// </summary>
        private void DrawCopyTextureTargetFolder()
        {
            EditorGUILayout.LabelField("设置拷贝至公共文件夹路径", headTitleStyle, GUILayout.Height(40));
            EditorGUILayout.LabelField("若需要将搜索图片拷贝成公共图片，并替换相关引用资产，则需要设置下面的文件夹，并以文件夹内的资产作为替换源");
            EditorGUI.BeginChangeCheck();
            copyTargetFolder = EditorGUILayout.ObjectField("拷贝存储目标文件夹", copyTargetFolder, typeof(DefaultAsset), false) as DefaultAsset;
            if (EditorGUI.EndChangeCheck())
            {
                if (copyTargetFolder != null)
                {
                    TryGetSearchTextureSimilarTexture();
                }
            }
        }

        private Vector2 copyTexScrollPos = Vector2.zero;
        // 若配置了拷贝目录文件夹，则将相似的纹理列出来
        private void DrawSimilarTextureInCopyFolder()
        {
            if (CollectingCacheData || copyTargetFolder == null) return;
            // 没有相似的图片，则提示是否需要拷贝创建
            if (copyFolderDoubleMatchedBatchData.Count <= 0)
            {
                GUI.enabled = searchTexBatchData != null;
                if (GUILayout.Button("当前目录下不存在与搜索图片相似的图片资产，是否拷贝一份搜索图片到该目录下？"))
                {
                    CopySearchTextureToCopyPath();
                }
                GUI.enabled = true;
                
                return;
            }
            // todo 因为实际项目中的公共图片还是太多了，需要改成按需加载
            copyTexScrollPos = EditorGUILayout.BeginScrollView(copyTexScrollPos,GUILayout.MinHeight(100), GUILayout.MaxHeight(300));
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (var batchData in copyFolderDoubleMatchedBatchData)
            {
                DrawTextureInfosAndReplaceBtn(batchData, false, true);
            }
            // 画一个空的添加按钮
            DrawTextureInfosAndReplaceBtn(null, false, true);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawReferencesInfos()
        {
            if (m_BatchAssetTreeView != null && m_BatchAssetTreeView.assetRoot != null && m_BatchAssetTreeView.assetRoot.children != null)
            {
                EditorGUILayout.BeginHorizontal(toolbarGUIStyle);
                if (GUILayout.Button("更新全局引用数据", toolbarButtonGUIStyle))
                {
                    bool option = EditorUtility.DisplayDialog("二次确认", 
                        "若替换过引用的资产，在更新全局引用后会导致当前已修改的部分无法回滚，是否确定？" , 
                        "Ok");
                    if (option)
                    {
                        parent.CollectDependenciesInfo();
                        needUpdateAssetTree = true;
                        EditorGUIUtility.ExitGUI();
                    }
                }
                //扩展
                if (GUILayout.Button("全部展开", toolbarButtonGUIStyle))
                {
                    if (m_BatchAssetTreeView != null) m_BatchAssetTreeView.ExpandAll();
                }
                //折叠
                if (GUILayout.Button("全部合并", toolbarButtonGUIStyle))
                {
                    if (m_BatchAssetTreeView != null) m_BatchAssetTreeView.CollapseAll();
                }
                EditorGUILayout.EndHorizontal();
            
                // 绘制Treeview
                m_BatchAssetTreeView.OnGUI(treeViewRect);
            }
        }
        
        #endregion

        #region 更新数据逻辑

        /// <summary>
        /// 当设置了需要拷贝纹理的文件夹后，查找此文件夹内是否有与SearchTexture相似的图
        /// 如果有，则自动将数据应用到copyBatchData，但仍然保留拷贝功能
        /// </summary>
        private void TryGetSearchTextureSimilarTexture()
        {
            checkTexList.Clear();
            checkTexPathList.Clear();
            copyFolderDoubleMatchedBatchData.Clear();
            string folderPath = AssetDatabase.GetAssetPath(copyTargetFolder);
            string sysPath = folderPath.Replace("Assets", Application.dataPath);
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
            
            // 检查此目录下的图片的特征值是否与当前搜索的图片有高度匹配的
            StartCheckCopyFolderTextureListCacheDataDelegate();
        }
        
        /// <summary>
        /// 开始预计算限制对比文件夹内的图片的特征值信息
        /// </summary>
        private void StartCheckCopyFolderTextureListCacheDataDelegate()
        {
            updateIndex = 0;
            updateTotalNum = checkTexPathList.Count;
            // 目录下没有图片
            if (updateTotalNum <= 0) return;
            CollectingCacheData = true;
            EditorApplication.update += CheckCopyFolderTextureListCacheDataDelegate;
        }
        
        /// <summary>
        /// 预计算限制对比文件夹内的图片的特征值
        /// </summary>
        private void CheckCopyFolderTextureListCacheDataDelegate()
        {
            string path = checkTexPathList[updateIndex];
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(string.Format("检查文件夹内的图片特征值({0}/{1})", updateIndex, updateTotalNum),
                path, (float)updateIndex / updateTotalNum);
            SimilarTextureCacheData cacheData = parent.cacheSimilarData.TryGetCacheData(path);
            if (cacheData == null)
            {
                cacheData = SimilarTextureCheckToolUtil.CreateNewCacheData(path);
                if(cacheData != null)
                    parent.tempCacheSimilarDict.Add(path, cacheData);
            }
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                EditorApplication.update -= CheckCopyFolderTextureListCacheDataDelegate;
                parent.SaveCacheData();

                // 进行下一步，预计算目标图片路径下的图片特征值信息
                updateIndex = 0;
                updateTotalNum = checkTexPathList.Count;
                // 先检查这批文件夹里面的图片是否已经有缓存特征值了
                EditorApplication.update += GetSearchTextureSimilarTexturesInCopyFolderDelegate;
            }
            if (isCancel)
            {
                EditorApplication.update -= CheckCopyFolderTextureListCacheDataDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                parent.SaveCacheData(); 
                updateIndex = 0;
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// 获取与目标图片的相似图片
        /// </summary>
        private void GetSearchTextureSimilarTexturesInCopyFolderDelegate()
        {
            string path = checkTexPathList[updateIndex];
            SimilarTextureCacheData cacheData = parent.cacheSimilarData.TryGetCacheData(path);
            bool isCancel = EditorUtility.DisplayCancelableProgressBar(
                string.Format("正在比对图片特征值({0}/{1})", updateIndex, updateTotalNum),
                path, (float)updateIndex / updateTotalNum);

            // 需要排除自己
            if (cacheData != null && !cacheData.texturePath.Equals(searchTexCacheData.texturePath))
            {
                int perceptualCounter = SimilarTextureCheckToolUtil.PerceptualDistance(searchTexCacheData.perceptualHash, cacheData.perceptualHash);
                double histogramSimilarity = SimilarTextureCheckToolUtil.CalcSimilarity(searchTexCacheData.ImageHistogramData, cacheData.ImageHistogramData);

                bool P_Match = perceptualCounter < parent.PerceptualHashThreshold;
                bool H_Match = histogramSimilarity >= parent.ImageHistogramThreshold;

                // 只留下满足两个判断的数据
                if (P_Match && H_Match)
                {
                    copyFolderDoubleMatchedBatchData.Add(new BatchData(path));
                }
            }
            
            updateIndex++;
            CheckGC();
            if (updateIndex >= updateTotalNum) // 计算完成
            {
                updateIndex = 0;
                EditorApplication.update -= GetSearchTextureSimilarTexturesInCopyFolderDelegate;
                CollectingCacheData = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
            if (isCancel)
            {
                EditorApplication.update -= GetSearchTextureSimilarTexturesInCopyFolderDelegate;
                EditorUtility.DisplayDialog("提示", "操作中止" , "Ok");
                updateIndex = 0;
                CollectingCacheData = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void CheckGC()
        {
            if (updateIndex % 2000 == 0)
                GC.Collect();
        }

        private void CopySearchTextureToCopyPath()
        {
            if (searchTexBatchData == null) return;
            
            string copyTargetPath = AssetDatabase.GetAssetPath(copyTargetFolder);
            string directory = copyTargetPath.Replace("Assets", Application.dataPath);
            bool succeed = SimilarTextureCheckToolUtil.CopyAndSaveTextureToTargetPath(searchTexBatchData.Tex, directory);
            if (succeed)
            {
                TryGetSearchTextureSimilarTexture();
            }
        }

        /// <summary>
        /// 将这个数据设置为用于批量替换的数据
        /// </summary>
        /// <param name="batchData"></param>
        private void SelectAsMainReplaceData(BatchData batchData, bool isCopyData)
        {
            if (CanRevertItemAllRefsByDict(guidReplacedDict) || CanRevertItemAllRefsByDict(guidAtlasReplacedDict))
            {
                bool confirm = EditorUtility.DisplayDialog("二次确认", 
                    "若替换过引用的资产，在切换替换源后会导致当前已修改的部分无法回滚，是否确定？" , 
                    "Ok");
                if (confirm)
                {
                    mainReplaceBatchData = batchData;
                    InitGUIDReplacedDict();
                }
                return;
            }
            mainReplaceBatchData = batchData;
            // 更新treeview
            if (replaceDataIsCopyData != isCopyData)
            {
                needUpdateAssetTree = true;
            }
            replaceDataIsCopyData = isCopyData;
        }

        /// <summary>
        /// 更新当前界面的引用关系数据(父节点会持有此window，用于统一调度更新)
        /// </summary>
        public void UpdateCurTextureReferences()
        {
            referenceDataLoadFinished = false;
            
            if (!parent.IsReferenceCollectFinished()) return;
            AssetDescription referenceData;
            if (doubleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in doubleMatchTexBatchDataList)
                {
                    if (!string.Empty.Equals(batchData.AtlasTexGUID))
                    {
                        referenceData = parent.GetReferenceData(batchData.AtlasTexGUID);
                        if (referenceData == null) return;
                    }
                    referenceData = parent.GetReferenceData(batchData.TexGUID);
                    if (referenceData == null) return;
                }
            }
            
            if (singleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in singleMatchTexBatchDataList)
                {
                    if (!string.Empty.Equals(batchData.AtlasTexGUID))
                    {
                        referenceData = parent.GetReferenceData(batchData.AtlasTexGUID);
                        if (referenceData == null) return;
                    }
                    referenceData = parent.GetReferenceData(batchData.TexGUID);
                    if (referenceData == null) return;
                }
            }
            
            referenceDataLoadFinished = true;
        }
        
        private void InitGUIDReplacedDict()
        {
            // 收集一次当前的引用资源数据，以便于做回滚操作
            // 但回滚的话有一个可能出现的情况，就是在一个资产中同时引用了与当前查找的图与相似图，使用GUID直接替换的话回滚会出现问题
            // 这种情况的出现概率很低，但又很难解决...
            if (referenceDataLoadFinished)
            {
                guidReplacedDict.Clear();
                guidAtlasReplacedDict.Clear();
                if (doubleMatchTexBatchDataList.Count > 0)
                {
                    foreach (var batchData in doubleMatchTexBatchDataList)
                    {
                        InitGUIDReplacedDict(batchData);
                    }
                }
            
                if (singleMatchTexBatchDataList.Count > 0)
                {
                    foreach (var batchData in singleMatchTexBatchDataList)
                    {
                        InitGUIDReplacedDict(batchData);
                    }
                }
                // 当前如果设置了拷贝公共文件夹的数据作为替换源，则需要把搜索图片的数据也作为替换内容添加修改缓存
                if (replaceDataIsCopyData && searchTexBatchData != null)
                {
                    InitGUIDReplacedDict(searchTexBatchData);
                }
            }
        }

        /// <summary>
        /// 收集当前匹配到的图片资产的引用信息，将引用的资产GUID和图片资产路径关联起来，做个替换记录
        /// </summary>
        /// <param name="batchData"></param>
        private void InitGUIDReplacedDict(BatchData batchData)
        {
            AssetDescription referenceData;
            if (!string.Empty.Equals(batchData.AtlasTexGUID))
            {
                referenceData = parent.GetReferenceData(batchData.AtlasTexGUID);
                foreach (var guid in referenceData.references)
                {
                    Dictionary<string, bool> atlasTempDic;
                    if (!guidAtlasReplacedDict.ContainsKey(guid))
                    {
                        guidAtlasReplacedDict.Add(guid, new Dictionary<string, bool>());
                    }
                    atlasTempDic = guidAtlasReplacedDict[guid];
                    atlasTempDic.Add(batchData.AtlasTexPath, false);
                }
            }
            referenceData = parent.GetReferenceData(batchData.TexGUID);
            foreach (var guid in referenceData.references)
            {
                Dictionary<string, bool> tempDic;
                if (!guidReplacedDict.ContainsKey(guid))
                {
                    guidReplacedDict.Add(guid, new Dictionary<string, bool>());
                }
                tempDic = guidReplacedDict[guid];
                tempDic.Add(batchData.TexPath, false);
            }
        }
        
        private void UpdateAssetTree()
        {
            if (needUpdateAssetTree && referenceDataLoadFinished && parent.IsReferenceCollectFinished())
            {
                var allShownAssets = AllReferenceToRootItem();
                InitGUIDReplacedDict();
                if(m_BatchAssetTreeView == null)
                {
                    //初始化TreeView
                    if (m_TreeViewState == null)
                        m_TreeViewState = new TreeViewState();
                    var headerState = BatchAssetTreeView.CreateDefaultMultiColumnHeaderState(position.width);
                    var multiColumnHeader = new MultiColumnHeader(headerState);
                    m_BatchAssetTreeView = new BatchAssetTreeView(m_TreeViewState, multiColumnHeader);
                }
                m_BatchAssetTreeView.assetRoot = allShownAssets;
                m_BatchAssetTreeView.CollapseAll();
                if(m_BatchAssetTreeView.assetRoot.children != null)
                    m_BatchAssetTreeView.Reload();
                needUpdateAssetTree = false;
            }
        }
        
        private BatchAssetViewItem AllReferenceToRootItem()
        {
            if (isSingleTexture)
            {
                return AllReferenceToRootItemForSingleTexData();
            }
            else
            {
                return AllReferenceToRootItemForMultiTexData();
            }
        }
        
        private BatchAssetViewItem AllReferenceToRootItemForSingleTexData()
        {
            int elementCount = 0;
            var root = new BatchAssetViewItem { id = elementCount, depth = -1, displayName = "Root" };
            int depth = 0;
            updatedAssetSet.Clear();
            var stack = new Stack<string>();
            
            // 当前搜索的图片数据整合treeChild
            if (replaceDataIsCopyData && searchTexBatchData != null)
            {
                BatchAssetViewItem child = CreateTree(searchTexBatchData.TexGUID, ref elementCount, depth, stack, searchTexBatchData);
                if (child != null)
                    root.AddChild(child);
                // 尝试添加图片对应的图集信息
                if (!string.Empty.Equals(searchTexBatchData.TexFileIdInAtlasTex))
                {
                    var atlasChild = CreateTree(searchTexBatchData.AtlasTexPath, ref elementCount, depth, stack, searchTexBatchData, true);
                    if (atlasChild != null)
                        root.AddChild(atlasChild);
                }
            }
            // 基本相同的图片数据整合treeChild
            if (doubleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in doubleMatchTexBatchDataList)
                {
                    BatchAssetViewItem child = CreateTree(batchData.TexGUID, ref elementCount, depth, stack, batchData);
                    if (child != null)
                        root.AddChild(child);
                    // 尝试添加图片对应的图集信息
                    if (!string.Empty.Equals(batchData.TexFileIdInAtlasTex))
                    {
                        var atlasChild = CreateTree(batchData.AtlasTexPath, ref elementCount, depth, stack, batchData, true);
                        if (atlasChild != null)
                            root.AddChild(atlasChild);
                    }
                }
            }
            
            // 可能相同的图片数据整合treeChild
            if (singleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in singleMatchTexBatchDataList)
                {
                    BatchAssetViewItem child = CreateTree(batchData.TexGUID, ref elementCount, depth, stack, batchData);
                    if (child != null)
                        root.AddChild(child);
                    // 尝试添加图片对应的图集信息
                    if (!string.Empty.Equals(batchData.TexFileIdInAtlasTex))
                    {
                        var atlasChild = CreateTree(batchData.AtlasTexPath, ref elementCount, depth, stack, batchData, true);
                        if (atlasChild != null)
                            root.AddChild(atlasChild);
                    }
                }
            }
            
            updatedAssetSet.Clear();
            return root;
        }

        private BatchAssetViewItem AllReferenceToRootItemForMultiTexData()
        {
            int elementCount = 0;
            var root = new BatchAssetViewItem { id = elementCount, depth = -1, displayName = "Root" };
            int depth = 0;
            updatedAssetSet.Clear();
            var stack = new Stack<string>();
            
            // 当前搜索的图片数据整合treeChild
            if (replaceDataIsCopyData && searchTexBatchData != null)
            {
                // 基本相同的图片数据整合treeChild
                elementCount++;
                BatchAssetViewItem titleSearch = new BatchAssetViewItem
                {
                    id = elementCount, 
                    depth = 0, 
                    displayName = "搜索图片的引用列表", 
                    window = this,
                    isDoubleMatchTitle = true
                };
                root.AddChild(titleSearch);
                BatchAssetViewItem child = CreateTree(searchTexBatchData.TexGUID, ref elementCount, depth + 1, stack, searchTexBatchData);
                if (child != null)
                    titleSearch.AddChild(child);
                // 尝试添加图片对应的图集信息
                if (!string.Empty.Equals(searchTexBatchData.TexFileIdInAtlasTex))
                {
                    var atlasChild = CreateTree(searchTexBatchData.AtlasTexPath, ref elementCount, depth + 1, stack, searchTexBatchData, true);
                    if (atlasChild != null)
                        titleSearch.AddChild(atlasChild);
                }
            }
            
            // 基本相同的图片数据整合treeChild
            elementCount++;
            BatchAssetViewItem titleDouble = new BatchAssetViewItem
            {
                id = elementCount, 
                depth = 0, 
                displayName = "基本相同的图片列表", 
                window = this,
                isDoubleMatchTitle = true
            };
            root.AddChild(titleDouble);
            if (doubleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in doubleMatchTexBatchDataList)
                {
                    BatchAssetViewItem child = CreateTree(batchData.TexGUID, ref elementCount, depth + 1, stack, batchData);
                    if (child != null)
                        titleDouble.AddChild(child);
                    // 尝试添加图片对应的图集信息
                    if (!string.Empty.Equals(batchData.TexFileIdInAtlasTex))
                    {
                        var atlasChild = CreateTree(batchData.AtlasTexPath, ref elementCount, depth + 1, stack, batchData, true);
                        if (atlasChild != null)
                            titleDouble.AddChild(atlasChild);
                    }
                }
            }
            
            // 可能相同的图片数据整合treeChild
            elementCount++;
            BatchAssetViewItem titleSingle = new BatchAssetViewItem
            {
                id = elementCount, 
                depth = 0, 
                displayName = "可能相同的图片列表", 
                window = this,
                isDoubleMatchTitle = false
            };
            root.AddChild(titleSingle);
            if (singleMatchTexBatchDataList.Count > 0)
            {
                foreach (var batchData in singleMatchTexBatchDataList)
                {
                    BatchAssetViewItem child = CreateTree(batchData.TexGUID, ref elementCount, depth + 1, stack, batchData);
                    if (child != null)
                        titleSingle.AddChild(child);
                    // 尝试添加图片对应的图集信息
                    if (!string.Empty.Equals(batchData.TexFileIdInAtlasTex))
                    {
                        var atlasChild = CreateTree(batchData.AtlasTexPath, ref elementCount, depth + 1, stack, batchData, true);
                        if (atlasChild != null)
                            titleSingle.AddChild(atlasChild);
                    }
                }
            }
            
            updatedAssetSet.Clear();
            return root;
        }
        
        //通过每个节点的数据生成子节点
        private BatchAssetViewItem CreateTree(string guid, ref int elementCount, int _depth, Stack<string> stack, 
            BatchData batchData, bool isAtlasRef = false)
        {
            if (stack.Contains(guid))
                return null;
            var referenceData = parent.GetReferenceData(guid);
            if (referenceData == null)
                return null;
            stack.Push(guid);
    
            ++elementCount;
            var root = new BatchAssetViewItem
            {
                id = elementCount, 
                displayName = referenceData.name, 
                data = referenceData, 
                batchData = batchData,
                depth = _depth, 
                window = this,
                isAtlasRef = isAtlasRef
            };
            foreach (var childGuid in referenceData.references)
            {
                var child = CreateTree(childGuid, ref elementCount, _depth + 1, stack, batchData, isAtlasRef);
                if (child != null)
                    root.AddChild(child);
            }

            stack.Pop();
            return root;
        }
        #endregion

        #region 资源操作逻辑

        public bool CanReplaceBatch(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null || item.children == null) return false;
            foreach (var child in item.children)
            {
                var childItem = child as BatchAssetViewItem;
                if (CanReplaceItemAllRefs(childItem)) return true;
            }

            return false;
        }
        
        public bool CanRevertBatch(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null || item.children == null) return false;
            foreach (var child in item.children)
            {
                var childItem = child as BatchAssetViewItem;
                if (CanRevertItemAllRefs(childItem)) return true;
            }

            return false;
        }
        public bool CanReplaceItemAllRefs(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return false;
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            return CanReplaceItemAllRefsByDict(targetDict);
        }
        public bool CanRevertItemAllRefs(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return false;
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            return CanRevertItemAllRefsByDict(targetDict);
        }

        public bool CanReplaceTarget(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return false;
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            string keyTexPath = item.isAtlasRef ? item.batchData.AtlasTexPath : item.batchData.TexPath;
            string guid = AssetDatabase.AssetPathToGUID(item.data.path);
            return (targetDict.ContainsKey(guid) && targetDict[guid].ContainsKey(keyTexPath)) ? !targetDict[guid][keyTexPath] : false;
        }

        public bool CanRevertTarget(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return false;
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            string keyTexPath = item.isAtlasRef ? item.batchData.AtlasTexPath : item.batchData.TexPath;
            string guid = AssetDatabase.AssetPathToGUID(item.data.path);
            return (targetDict.ContainsKey(guid) && targetDict[guid].ContainsKey(keyTexPath)) ? targetDict[guid][keyTexPath] : false;
        }

        /// <summary>
        /// 一键替换对应的相似图片类型的所有引用
        /// </summary>
        public void ReplaceAllByMatchType(BatchAssetViewItem item, bool deleteRefRes = false)
        {
            if (mainReplaceBatchData == null || item.children == null) return;
            foreach (var child in item.children)
            {
                var childItem = child as BatchAssetViewItem;
                ModifyAllByRefItem(childItem, true, false, deleteRefRes);
            }
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            // 可能需要重载一次引用信息
            UpdateBatchDataAfterDeleteRes();
        }
        /// <summary>
        /// 一键回滚对应的相似图片类型的所有引用
        /// </summary>
        public void RevertAllByMatchType(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null || item.children == null) return;
            foreach (var child in item.children)
            {
                var childItem = child as BatchAssetViewItem;
                ModifyAllByRefItem(childItem, false, false);
            }
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }
        
        /// <summary>
        /// 一键替换item的全部相关图片资源引用
        /// </summary>
        /// <param name="item"></param>
        public void ReplaceAllByRefItem(BatchAssetViewItem item, bool deleteRefRes = false)
        {
            if (mainReplaceBatchData == null) return;
            ModifyAllByRefItem(item, true, true, deleteRefRes);
        }

        /// <summary>
        /// 一键回滚item的全部相关图片资源引用
        /// </summary>
        /// <param name="item"></param>
        public void RevertAllByRefItem(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return;
            ModifyAllByRefItem(item, false);
        }

        public void ReplaceTarget(BatchAssetViewItem item)
        {
            if (mainReplaceBatchData == null) return;
            string guid = AssetDatabase.AssetPathToGUID(item.data.path);
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            
            if (targetDict.TryGetValue(guid, out Dictionary<string, bool> tempDict))
            {
                string fromGUID = item.isAtlasRef ? item.batchData.AtlasTexGUID : item.batchData.TexGUID;
                string toGUID = item.isAtlasRef ? mainReplaceBatchData.AtlasTexGUID : mainReplaceBatchData.TexGUID;
                string fromFileId = item.isAtlasRef ? item.batchData.TexFileIdInAtlasTex : String.Empty;
                string toFileId = item.isAtlasRef ? mainReplaceBatchData.TexFileIdInAtlasTex : String.Empty;
                string keyTexPath = item.isAtlasRef ? item.batchData.AtlasTexPath : item.batchData.TexPath;
                if (SimilarTextureCheckToolUtil.ModifyTargetAssetGUIDReference(guid, fromGUID, toGUID, fromFileId, toFileId, true))
                {
                    if (tempDict.ContainsKey(keyTexPath))
                    {
                        tempDict[keyTexPath] = true;
                    }
                    else
                    {
                        tempDict.Add(keyTexPath, true);
                    }
                }
            }
        }

        public void RevertTarget(BatchAssetViewItem item)
        {
            string guid = AssetDatabase.AssetPathToGUID(item.data.path);
            var targetDict = item.isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            if (targetDict.TryGetValue(guid, out Dictionary<string, bool> tempDict))
            {
                string fromGUID = item.isAtlasRef ? mainReplaceBatchData.AtlasTexGUID : mainReplaceBatchData.TexGUID;
                string toGUID = item.isAtlasRef ? item.batchData.AtlasTexGUID : item.batchData.TexGUID;
                string fromFileId = item.isAtlasRef ? mainReplaceBatchData.TexFileIdInAtlasTex : String.Empty;
                string toFileId = item.isAtlasRef ? item.batchData.TexFileIdInAtlasTex : String.Empty;
                string keyTexPath = item.isAtlasRef ? item.batchData.AtlasTexPath : item.batchData.TexPath;
                if (SimilarTextureCheckToolUtil.ModifyTargetAssetGUIDReference(guid, fromGUID, toGUID, fromFileId, toFileId, true))
                {
                    if (tempDict.ContainsKey(keyTexPath))
                    {
                        tempDict[keyTexPath] = false;
                    }
                    else
                    {
                        tempDict.Add(keyTexPath, false);
                    }
                }
            }
        }

        private bool CanReplaceItemAllRefsByDict(Dictionary<string, Dictionary<string,bool>> replacedDict)
        {
            foreach (var kv in replacedDict)
            {
                foreach (var dict in kv.Value)
                {
                    if (!dict.Value) return true;
                }
            }
            return false;
        }
        private bool CanRevertItemAllRefsByDict(Dictionary<string, Dictionary<string,bool>> replacedDict)
        {
            foreach (var kv in replacedDict)
            {
                foreach (var dict in kv.Value)
                {
                    if (dict.Value) return true;
                }
            }
            return false;
        }

        private void ModifyAllByRefItem(BatchAssetViewItem item, bool isReplace, bool refreshAsset = true, bool deleteRefRes = false)
        {
            ModifyAllByRefItem(item.batchData, item.isAtlasRef, isReplace, refreshAsset, deleteRefRes);
        }

        /// <summary>
        /// 一键操作所有引用资产引用
        /// </summary>
        /// <param name="isAtlasRef">true时为图集引用替换</param>
        /// <param name="isReplace">true时为全部替换，false时为全部回滚</param>
        /// <param name="refreshAsset">true时为刷新一次界面，优化项</param>
        /// <param name="deleteRefRes">true时会在替换完成后删除旧资源，不支持图集删除（容易误删），且仅在isReplace为true时生效，默认不开</param>
        private void ModifyAllByRefItem(BatchData batchData, bool isAtlasRef, bool isReplace, bool refreshAsset = true,
            bool deleteRefRes = false)
        {
            string cGUID = isAtlasRef ? batchData.AtlasTexGUID : batchData.TexGUID;
            string sGUID = isAtlasRef ? mainReplaceBatchData.AtlasTexGUID : mainReplaceBatchData.TexGUID;
            string cFileId = isAtlasRef ? batchData.TexFileIdInAtlasTex : String.Empty;
            string sFileId = isAtlasRef ? mainReplaceBatchData.TexFileIdInAtlasTex : String.Empty;
            string keyTexPath = isAtlasRef ? batchData.AtlasTexPath : batchData.TexPath;
            
            string fromGUID = isReplace ? cGUID : sGUID;
            string toGUID = isReplace ? sGUID : cGUID;
            string formFileId = isReplace ? cFileId : sFileId;
            string toFileId = isReplace ? sFileId : cFileId;
            var targetDict = isAtlasRef ? guidAtlasReplacedDict : guidReplacedDict;
            Dictionary<string, Dictionary<string, bool>> tempResultDic = new Dictionary<string, Dictionary<string, bool>>();

            // 遍历当前所有的GUID字典，找到这个GUID中是否有关于keyTexPath的引用
            // 如果有，就替换或回滚这个GUID中的对应的图片引用
            foreach (var kv in targetDict)
            {
                foreach (var kv2 in kv.Value)
                {
                    if (!keyTexPath.Equals(kv2.Key) || kv2.Value == isReplace) continue;
                    var replaced = SimilarTextureCheckToolUtil.ModifyTargetAssetGUIDReference(kv.Key, fromGUID, toGUID, formFileId, toFileId, false);
                    tempResultDic.TryAdd(kv.Key, new Dictionary<string, bool>());
                    tempResultDic[kv.Key].TryAdd(keyTexPath, replaced);
                }
            }
            
            // 替换并且需要删除旧资源
            if (isReplace && deleteRefRes && !isAtlasRef)
            {
                // 更新操作记录，但与替换不同，这里直接删除记录，不再提供回滚和替换操作
                foreach (var kv in tempResultDic)
                {
                    foreach (var kv2 in kv.Value)
                    {
                        if (targetDict.ContainsKey(kv.Key) && targetDict[kv.Key].ContainsKey(kv2.Key) && kv2.Value)
                        {
                            targetDict[kv.Key].Remove(kv2.Key);
                        }
                    }
                }
                // 删除当前被替换的资产，并记录被删除的资源路径，用于做面板的数据收集剔除
                AssetDatabase.DeleteAsset(batchData.TexPath); // todo 或许收集起来再删除更好？
                deleteResPath.TryAdd(batchData.TexPath, true);
            }
            else
            {
                // 普通的更新操作记录，用于支持回滚和可替换判断
                foreach (var kv in tempResultDic)
                {
                    foreach (var kv2 in kv.Value)
                    {
                        if (targetDict.ContainsKey(kv.Key) && targetDict[kv.Key].ContainsKey(kv2.Key) && kv2.Value)
                        {
                            targetDict[kv.Key][kv2.Key] = isReplace;
                        }
                    }
                }
            }
            if (refreshAsset)
            {
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
            }
            if (refreshAsset && isReplace && deleteRefRes && !isAtlasRef)
            {
                UpdateBatchDataAfterDeleteRes();
            }
        }

        /// <summary>
        /// 删除引用资源后，强制更新一波缓存数据和界面内容
        /// </summary>
        private void UpdateBatchDataAfterDeleteRes()
        {
            // 删除父节点界面缓存结果中的相关内容
            parent.UpdateBatchDataAfterDeleteRes(index, deleteResPath);
            // 重载一次引用信息
            RecollectBatchDatas();
            if (searchTexBatchData == null)
            {
                parent.MarkDeletedSearchItem(index);
            }
            UpdateCurTextureReferences();
            needUpdateAssetTree = true;
        }
        #endregion
        #region 其他逻辑

        private void OnDisable()
        {
            if (parent != null)
            {
                
            }
        }

        #endregion
    }
}
