using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SimilarTextureCheckTool
{
    public class SimilarTextureCheckToolUtil
    {
        public static readonly string CacheDataSavePath = "Library/SimilarTextureCheckToolCacheData"; // 二进制序列化
        public static readonly string ModifyPathsSavePath = "Library/ModifyPathsCacheData"; // 修改到的文件缓存路径
        
        public static readonly string ImageHistogramThresholdKey = "ImageHistogramThresholdKey";
        public static readonly double ImageHistogramThresholdDefault = 0.9d;
        public static readonly string PerceptualHashThresholdKey = "PerceptualHashThresholdKey";
        public static readonly int PerceptualHashThresholdDefault = 3;
        public static readonly string LimitCompareTexturePathName = "LimitCompareTexturePathName";
        public static readonly string AtlasTextureRootPathName = "AtlasTextureRootPathName";
        public static readonly string SourceTextureRootPathName = "SourceTextureRootPathName";
        
        public static readonly string SpriteWarningTips = @"由于sprite和图集的特殊性，即图集由多张源texture打包生成
而sprite指向图集而非源texture，因此无法从sprite追溯到源texture
因此，在使用本工具前，默认assets内图集和源texture都还存在，且已经将图集和源texture分开存储
这里需要使用前标记好项目中的atlas和源texture的目录，以便sprite类型的图片可以正确查找相似图片";

        public static readonly string DoubleMatchTitle = "基本相同的图片资产列表(点击图片单个操作)";
        public static readonly string SingleMatchTitle = "有可能相同的图片资产列表(两个对比算法只满足了一个，点击图片单个操作)";
        public static readonly string[] TextureExtensions = new string[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

        private static string PerceptualHashCSPath = "Assets/SimilarTextureCheckTool/ComputeShader/PerceptualHashCS.compute";
        private static string ImageHistogramCSPath = "Assets/SimilarTextureCheckTool/ComputeShader/ImageHistogramCS.compute";
        private static ComputeShader PerceptualHashCS;
        private static ComputeShader ImageHistogramCS;
        private static int _TexResId = Shader.PropertyToID("_TexRes");
        private static int _DCTDataId = Shader.PropertyToID("_DCTData");
        private static int _HistogramDataIntId = Shader.PropertyToID("_HistogramDataInt");
        private static int _TexexParamsId = Shader.PropertyToID("_TexexParams");
        private static Texture2D tempPerceptualHashTexture;
        private static int DCTDataNum = 32 * 32;
        private static int IHBin = 8; // 与CS中对应
        private static int IHThreadNum = 16; // IH线程数，与CS对应

        #region 公共方法
        
        /// <summary>
        /// 将一份纹理复制并存储到指定目录下
        /// </summary>
        /// <param name="tex">要被复制的图片</param>
        /// <param name="directory">存储图片的系统路径</param>
        /// <returns></returns>
        public static bool CopyAndSaveTextureToTargetPath(Texture2D tex, string directory)
        {
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string formatName = Enum.GetName(typeof(RenderTextureFormat), rt.format);
            TextureFormat? texFormat = null;
            foreach (TextureFormat f in Enum.GetValues(typeof(TextureFormat)))
            {
                string fName = Enum.GetName(typeof(TextureFormat), f);
                if (fName.Equals(formatName, StringComparison.Ordinal))
                {
                    texFormat = f;
                    break;
                }
            }

            string finalSavePath = String.Empty;
            bool succeed = false;
            if (texFormat != null)
            {
                RenderTexture.active = rt;
                Graphics.Blit(tex, rt);
                Texture2D copyTex = new Texture2D(tex.width, tex.height, (TextureFormat) texFormat, tex.mipmapCount > 1, true);
                copyTex.name = tex.name;
                
                copyTex.ReadPixels(new Rect(0.0f, 0.0f, tex.width, tex.height), 0, 0);
                copyTex.Apply();
                
                RenderTexture.active = null;
                // 打开保存文件的对话框，用户可指定文件名和位置
                finalSavePath = EditorUtility.SaveFilePanel("复制当前搜索图片", directory, 
                    "copy_" + copyTex.name + ".png", "png");
                // 将Texture2D转换为PNG格式的字节数据
                byte[] pngData = copyTex.EncodeToPNG();
                if (pngData != null)
                {
                    // 写入文件
                    File.WriteAllBytes(finalSavePath, pngData);

                    // 这里可以加入你的“完成回调”逻辑
                    Debug.LogFormat("Texture 存储在 {0}", finalSavePath);
                    AssetDatabase.Refresh();
                    // 检查是否存储在目标的路径下，否则弹出提示但不拦截
                    succeed = finalSavePath.Contains(directory);
                    if (!succeed)
                    {
                        Debug.LogErrorFormat("Texture {0} 存储路径无效，需要存储在 {1}", tex.name, directory);
                    }
                }
            }
            RenderTexture.ReleaseTemporary(rt);
            return succeed;
        }
        
        /// <summary>
        /// 修改传入的GUID的相关引用资源，将fromGUID的引用替换成toGUID的引用并根据是否是单个操作执行保存
        /// </summary>
        /// <param name="guid">目标资产GUID</param>
        /// <param name="fromGUID">GUID引用的，将被替换的资产GUID</param>
        /// <param name="toGUID">将fromGUID替换成此toGUID所代表的资产</param>
        /// <param name="fromFileId">图集情况时有效，将被替换掉的图集FileId</param>
        /// <param name="toFileId">图集情况时有效，用于替换fromFileId</param>
        /// <param name="isSingle">是否是单个操作，true时修改后会自动保存</param>
        /// <returns></returns>
        public static bool ModifyTargetAssetGUIDReference(
            string guid, 
            string fromGUID, 
            string toGUID, 
            string fromFileId, 
            string toFileId, 
            bool isSingle)
        {
            bool succeed = false;
            string relatePath = AssetDatabase.GUIDToAssetPath(guid);
            string sysPath = relatePath.Replace("Assets", Application.dataPath);
            bool needReplaceFileId = !string.Empty.Equals(fromFileId) && !string.Empty.Equals(toFileId); 
            

            if (needReplaceFileId)
            {
                // 按行读取文件内容
                string[] lines = File.ReadAllLines(sysPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string lineStr = lines[i];
                    if (Regex.IsMatch(lineStr, fromGUID) && Regex.IsMatch(lineStr, fromFileId))
                    {
                        lines[i] = lineStr.Replace(fromGUID, toGUID).Replace(fromFileId, toFileId);
                        succeed = true;
                    }
                    File.WriteAllLines(sysPath, lines);
                }
            }
            else
            {
                var content = File.ReadAllText(sysPath);
                if (Regex.IsMatch(content, fromGUID))
                {
                    content = content.Replace(fromGUID, toGUID);  
                    File.WriteAllText(sysPath, content);
                    succeed = true;
                }
            }
            if (!succeed)
            {
                string formResPath = AssetDatabase.GUIDToAssetPath(fromGUID);
                string toResPath = AssetDatabase.GUIDToAssetPath(toGUID);
                Debug.LogWarningFormat("资产 {0} 中,所引用的资产 {1} 替换成 {2} ，操作失败", relatePath, formResPath, toResPath);
            }
            if (succeed && isSingle)
            {
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
            }
            return succeed;
        }

        /// <summary>
        /// 传入的相对路径对应的对象是否是一个文件夹对象
        /// </summary>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        public static bool IsFolderAsset(string assetPath)
        {
            // 使用AssetDatabase的GetMainAssetTypeAtPath方法获取路径指向的主资产类型
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            return assetType == typeof(DefaultAsset) && System.IO.Directory.Exists(assetPath);
        }
        /// <summary>
        /// 创建一份新的图片特征值缓存
        /// </summary>
        /// <param name="texPath">资产相对路径</param>
        /// <returns></returns>
        public static SimilarTextureCacheData CreateNewCacheData(string texPath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            return CreateNewCacheData(texPath, texture);
        }
        public static SimilarTextureCacheData CreateNewCacheData(string texPath, Texture2D texture)
        {
            if (texture != null)
            {
                var inst = new SimilarTextureCacheData();
                inst.texturePath = texPath;
                inst.perceptualHash = CalculateTexturePerceptualHash(texture);
                inst.ImageHistogramData = CalculateTextureImageHistogram(texture).ToList();
                return inst;
            }
            else
            {
                Debug.LogErrorFormat("图片加载失败，请确认路径:“{0}” 是否正确！", texPath);
                return null;
            }
        }

        /// <summary>
        /// 是否配置了图集和源texture路径
        /// </summary>
        /// <param name="atlasTextureFolderPath">当前配置的图集路径</param>
        /// <param name="sourceTextureFolderPath">当前配置的源texture路径</param>
        /// <returns></returns>
        public static bool IsAtlasAndSourceTextureRootFolderConfigure(
            out string atlasTextureFolderPath,
            out string sourceTextureFolderPath)
        {
            atlasTextureFolderPath = EditorPrefs.GetString(AtlasTextureRootPathName);
            sourceTextureFolderPath = EditorPrefs.GetString(SourceTextureRootPathName);
            return !string.Empty.Equals(atlasTextureFolderPath)
                   && !string.Empty.Equals(sourceTextureFolderPath)
                   && IsFolderAsset(atlasTextureFolderPath)
                   && IsFolderAsset(sourceTextureFolderPath);
        }

        /// <summary>
        /// 通过sprite回溯到源texture的方法
        /// 该方法可能需要根据不同项目进行客制化
        /// </summary>
        /// <param name="sprite"></param>
        /// <returns></returns>
        public static Texture2D TryGetSourceTextureBySprite(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            
            string atlasTextureFolderPath, sourceTextureFolderPath;
            if (IsAtlasAndSourceTextureRootFolderConfigure(out atlasTextureFolderPath, out sourceTextureFolderPath))
            {
                string textureName = sprite.name;
                string atlasName = sprite.texture.name;
                // 查找方法1：先在源texture路径下找到完全符合atlasName的文件夹路径，缩小针对textureName的遍历范围
                var allFolderPath = Directory.GetDirectories(sourceTextureFolderPath);
                Texture2D retTexture = null;
                foreach(var path in allFolderPath)
                {
                    if (path.Contains(atlasName))
                    {
                        string matchedPath = path.Replace("\\", "/");
                        retTexture = TryFindTextureByNameAndPath(textureName, matchedPath);
                        if (retTexture != null) break;
                    }
                }

                if (retTexture != null) return retTexture;
                
                // 查找方法2：暴力遍历源texture路径，直到找到同名texture为止
                return TryFindTextureByNameAndPath(textureName, sourceTextureFolderPath);
            }

            return null;
        }

        /// <summary>
        /// 根据传入的源texture，尝试获取到对应的图集资源，否则返回自己
        /// </summary>
        /// <param name="tex"></param>
        /// <returns></returns>
        public static Texture2D TryGetAtlasTextureBySourceTexture(Texture2D tex)
        {
            if (tex == null) return null;
            string atlasTextureFolderPath, sourceTextureFolderPath;
            if (IsAtlasAndSourceTextureRootFolderConfigure(out atlasTextureFolderPath, out sourceTextureFolderPath))
            {
                string texturePath = AssetDatabase.GetAssetPath(tex);
                if (string.Empty.Equals(texturePath) || IsAtlasTexture(texturePath)) 
                    return tex;
            
                // 这里默认图片所在文件夹名称就是图集名称，如果项目命名不规范这个地方很难办
                string folderName = Path.GetDirectoryName(texturePath);
                folderName = folderName.Substring(folderName.LastIndexOf("\\") + 1);
                Texture2D retTexture = null;
                // 这里直接遍历所有的图集，因为图集的组织更难规范
                var allAtlasTexturePaths = Directory.GetFiles(atlasTextureFolderPath, "*.*", SearchOption.AllDirectories)
                    .Where(s => TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
                foreach (var rawPath in allAtlasTexturePaths)
                {
                    string atlasPath = rawPath.Replace("\\", "/");
                    string fileName = Path.GetFileNameWithoutExtension(atlasPath);
                    if (folderName.Equals(fileName))
                    {
                        retTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                        if(retTexture != null)
                            break;
                    }
                }

                return retTexture != null ? retTexture : tex;
            }

            // 没有配置图集和源texture文件夹的话，返回自己
            return tex;
        }

        /// <summary>
        /// 根据传入的源texture，计算出它可能对应的图集，以及此源texture在图集内对应的fileId
        /// 主要用于找到相似图后，进行替换时，计算出目标的图集GUID和图集内部的fileId进行直接写入YAML替换引用
        /// </summary>
        /// <param name="tex">用于查找图集的源texture</param>
        /// <param name="atlasTexture">根据算法匹配到的图集，但需要结合fileId判断是否是正确的图集</param>
        /// <param name="fileId">不为空时，atlasTexture才有效，代表这个tex在atlasTexture中的fileId</param>
        public static void TryGetSpriteFileIdInAtlasTextureBySourceTexture(Texture2D tex, out Texture2D atlasTexture, out string fileId)
        {
            fileId = String.Empty;
            atlasTexture = TryGetAtlasTextureBySourceTexture(tex);
            if (tex == atlasTexture) return;
            // 根据逐行查找对应图集的meta文件，找到相同与tex图片名称的fileId
            string metaPath = AssetDatabase.GetAssetPath(atlasTexture) + ".meta";
            if (!File.Exists(metaPath)) return;
            // 按行读取.meta文件内容
            string[] lines = File.ReadAllLines(metaPath);
            bool reachNameTable = false;
            string matchingStr = tex.name + ": ";
            foreach (var line in lines)
            {
                if (reachNameTable && line.Contains(matchingStr)) // 找到 图片名称:fileId 的内容时，抽取fileId
                {
                    fileId = line.Trim().Substring(matchingStr.Length);
                    break;
                }
                if (!reachNameTable && line.Trim().Equals("nameFileIdTable:")) reachNameTable = true;
            }
        }

        public static Texture2D TryFindTextureByNameAndPath(string textureName, string path)
        {
            var allTexturePath = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(s => TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
            string actualFilePath = string.Empty;
            foreach (var texPath in allTexturePath)
            {
                string texFileName = Path.GetFileNameWithoutExtension(texPath);
                if (textureName.Equals(texFileName))
                {
                    actualFilePath = texPath.Replace("\\", "/");
                    break;
                }
            }

            if (string.Empty.Equals(actualFilePath)) return null;
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(actualFilePath);
            return tex;
        }

        /// <summary>
        /// 判断传递进来的图是不是图集对象
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public static bool IsAtlasTexture(Texture2D texture)
        {
            if (texture == null) return false;
            
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return IsAtlasTexture(assetPath);
        }
        public static bool IsAtlasTexture(string assetPath)
        {
            if (string.Empty.Equals(assetPath)) return false;
            
            // 如果项目严格组织图集路径，下面这段代码理应不会出现，况且AssetImporter.GetAtPath有一定开销，因此能不用就不用
            // TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            // if (ti.textureType != TextureImporterType.Sprite) return false;
            
            string atlasTextureFolderPath, sourceTextureFolderPath;
            if (IsAtlasAndSourceTextureRootFolderConfigure(out atlasTextureFolderPath, out sourceTextureFolderPath))
            {
                return assetPath.Contains(atlasTextureFolderPath);
            }
            return false;
        }
        
        /// <summary>
        /// 判断这个texture是否是图集，如果是则返回其相关的源texture，替换掉调度处的目标texture
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public static List<Texture2D> TryGetAtlasAllSourceTextures(Texture2D texture)
        {
            if (!IsAtlasTexture(texture)) return null;
            List<Texture2D> retList = new List<Texture2D>();
            
            // 此处默认图集名称就是源texture文件夹的名称，需要根据实际情况调整逻辑
            string targetFolderName = texture.name;
            // 能走到这一步的话，说明这个一定是文件夹路径
            string sourceTextureFolderPath = EditorPrefs.GetString(SourceTextureRootPathName); 
            var allFolderPath = Directory.GetDirectories(sourceTextureFolderPath);
            foreach(var rawPath in allFolderPath)
            {
                string path = rawPath.Replace("\\", "/");
                string folderName = Path.GetFileName(path);
                if (targetFolderName.Equals(folderName))
                {
                    TryAppendAllTexturesFormSourceTextureFolder(path, ref retList);
                    break;
                }
            }
            
            return retList.Count > 0 ? retList : null;
        }

        public static void TryAppendAllTexturesFormSourceTextureFolder(string folderPath, ref List<Texture2D> list)
        {
            var allTexturePath = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(s => TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
            foreach (var rawPath in allTexturePath)
            {
                string path = rawPath.Replace("\\", "/");
                Texture2D tex2D = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex2D != null)
                {
                    list.Add(tex2D);
                }
            }
        }

        /// <summary>
        /// 根据源图片，判断是否是图集的源图片，并尝试获取到目标图集并找到对应的sprite对象
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public static Sprite TryGetSpriteBySourceTexture(Texture2D texture)
        {
            if (texture == null) 
                return null;
            
            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (string.Empty.Equals(texturePath) || !IsAtlasTexture(texturePath)) 
                return null;
            
            // 这里默认图片所在文件夹名称就是图集名称，如果项目命名不规范这个地方很难办
            string folderName = Path.GetDirectoryName(texturePath);
            folderName = folderName.Substring(folderName.LastIndexOf("\\") + 1);
            
            // 能走到这一步的话，说明这个一定是文件夹路径
            string atlasTextureFolderPath = EditorPrefs.GetString(AtlasTextureRootPathName); 
            // 这里直接遍历所有的图集，因为图集的组织更难规范
            var allAtlasTexturePaths = Directory.GetFiles(atlasTextureFolderPath, "*.*", SearchOption.AllDirectories)
                .Where(s => TextureExtensions.Contains(Path.GetExtension(s).ToLower())).ToArray();
            List<UnityEngine.Object> allSprites = new List<UnityEngine.Object>();
            foreach (var rawPath in allAtlasTexturePaths)
            {
                string atlasPath = rawPath.Replace("\\", "/");
                string fileName = Path.GetFileNameWithoutExtension(atlasPath);
                if (folderName.Equals(fileName))
                {
                    var objs = AssetDatabase.LoadAllAssetRepresentationsAtPath(atlasPath);
                    if (objs != null && objs.Length > 0)
                    {
                        allSprites.AddRange(objs);
                    }
                    break;
                }
            }
            if (allSprites.Count <= 0) 
                return null;
            
            Sprite targetSprite = null;
            string spriteName = texture.name;
            for (int i = 0; i < allSprites.Count; i++)
            {
                Sprite tempSprite = allSprites[i] as Sprite;
                if (tempSprite != null && spriteName.Equals(tempSprite.name))
                {
                    targetSprite = tempSprite;
                    break;
                }
            }

            return targetSprite;
        }
        
        /// <summary>
        /// 将图片计入目标图片列表的入口，这里会统一检查图片是否是图集，如果是图集，则需要去获取图集对应的源textures
        /// </summary>
        /// <param name="tex">将用来对比的图片</param>
        /// <param name="texPath">将用来对比的图片的资产相对路径</param>
        /// <param name="texList">目标存储的图片资源List</param>
        /// <param name="texPathList">目标存储的图片资源的路径List</param>
        public static void AppendTextureToCheckTextureList(Texture2D tex, string texPath, ref List<Texture2D> texList, ref List<string> texPathList)
        {
            List<Texture2D> sourceTextures = TryGetAtlasAllSourceTextures(tex);
            if (sourceTextures != null)
            {
                foreach (var sourceTex in sourceTextures)
                {
                    string sourceTexPath = AssetDatabase.GetAssetPath(sourceTex);
                    texList.Add(sourceTex);
                    texPathList.Add(sourceTexPath);
                }
            }
            else
            {
                texList.Add(tex);
                texPathList.Add(texPath);
            }
        }
        
        #endregion
        
        #region Perceptual Hash
        private static void LoadPerceptualHashCS()
        {
    #if UNITY_EDITOR
            if (PerceptualHashCS == null)
            {
                PerceptualHashCS = AssetDatabase.LoadAssetAtPath<ComputeShader>(PerceptualHashCSPath);
            }
    #endif
        }

        public static string CalculateTexturePerceptualHash(Texture2D tex, bool logout = false)
        {
            LoadPerceptualHashCS();
            if(PerceptualHashCS == null || tex == null) return String.Empty;
            // 数据准备
            // 重新调整贴图尺寸
            ResizeTexture(tex, 32, 32);
            float[] dctData = new float[DCTDataNum];
            ComputeBuffer cb = new ComputeBuffer(32 * 32, 4);
            cb.SetData(dctData);
            // 调度
            PerceptualHashCS.SetTexture(0, _TexResId, tempPerceptualHashTexture);
            PerceptualHashCS.SetBuffer(0, _DCTDataId, cb);
            PerceptualHashCS.Dispatch(0, 1, 1, 1);
            // 数据收集
            cb.GetData(dctData);
            cb.Dispose();
            if (logout)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(string.Format("texture name = {0},dctData = ", tex.name));
                for (int i = 0; i < dctData.Length; i++)
                {
                    sb.Append(string.Format("\ni = {0}, data = {1}", i, dctData[i]));
                }
                Debug.Log(sb);
            }
            return GetPerceptualHash(dctData);
        }
        
        public static Texture2D ResizeTexture(Texture2D texture2D, int targetX, int targetY, bool mipmap = false)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetX, targetY, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string formatName = Enum.GetName(typeof(RenderTextureFormat), rt.format);
            TextureFormat? texFormat = null;
            foreach (TextureFormat f in Enum.GetValues(typeof(TextureFormat)))
            {
                string fName = Enum.GetName(typeof(TextureFormat), f);
                if (fName.Equals(formatName, StringComparison.Ordinal))
                {
                    texFormat = f;
                    break;
                }
            }

            if (texFormat != null)
            {
                RenderTexture.active = rt;
                Graphics.Blit(texture2D, rt);

                if (tempPerceptualHashTexture == null)
                {
                    tempPerceptualHashTexture = new Texture2D(targetX, targetY, (TextureFormat) texFormat, 0, true);
                }
                else
                {
                    tempPerceptualHashTexture.Reinitialize(targetX, targetY, (TextureFormat)texFormat, true);
                }
                tempPerceptualHashTexture.ReadPixels(new Rect(0.0f, 0.0f, targetX, targetY), 0, 0);
                tempPerceptualHashTexture.Apply();
                
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            else
            {
                Debug.LogFormat("图片格式错误,图片名：{0}", texture2D.name);
            }
            return tempPerceptualHashTexture;
        }

        /// <summary>
        /// 根据分析图片计算出来的DCT数据，计算出对应的Hash
        /// </summary>
        /// <param name="dctData">一维化的dct数据，尺寸为32x32</param>
        /// <returns></returns>
        public static string GetPerceptualHash(float[] dctData)
        {
            string h = "";
            // 缩小DCT数据尺寸，只计算8x8区域的内容，即DCT数据左上角的部分,但要去掉（0，0）点的数据
            // 这部分代表了图片中的低频信息
            float total = 0;
            for(int i = 0; i < 8; i ++)
            {
                for(int j = 0; j < 8; j ++)
                {
                    if (i != 0 && j != 0)
                    {
                        int id = j * 32 + i;
                        total += dctData[id];
                    }
                }
            }
            float avg = total / 63;

            for(int i = 0; i < 8; i ++)
            {
                for(int j = 0; j < 8; j ++)
                {
                    if (i != 0 && j != 0)
                    {
                        int id = j * 32 + i;
                        h += (dctData[id] > avg ? "1" : "0");
                    }
                }
            }
            return h;
        }

        public static int PerceptualDistance(string hashA, string hashB)
        {
            if (string.Empty.Equals(hashA) || string.Empty.Equals(hashB)) return 32;
            
            return PerceptualDistance(hashA.ToCharArray(), hashB.ToCharArray());
        }
        public static int PerceptualDistance(char[] charA, char[] charB)
        {
            int counter = 0;
            for (int k = 0; k < charA.Length; k++) {
                if (charA[k] != charB[k]) {
                    counter++;
                }
            }
            return counter;
        }

        #endregion

        #region Image Histogram

        private static void LoadImageHistogramCS()
        {
    #if UNITY_EDITOR
            if (ImageHistogramCS == null)
            {
                ImageHistogramCS = AssetDatabase.LoadAssetAtPath<ComputeShader>(ImageHistogramCSPath);
            }
    #endif
        }
        
        public static double[] CalculateTextureImageHistogram(Texture2D tex, bool logout = false)
        {
            LoadImageHistogramCS();
            if (ImageHistogramCS == null || tex == null) return null;
            // 数据准备
            int histogramBufferCount = IHBin * IHBin * IHBin;
            int[] histogramBufferInt = new int[histogramBufferCount];
            ComputeBuffer cb = new ComputeBuffer(histogramBufferCount, 4);
            cb.SetData(histogramBufferInt);
            // 调度
            ImageHistogramCS.SetTexture(0, _TexResId, tex);
            ImageHistogramCS.SetVector(_TexexParamsId, new Vector4(tex.width, tex.height, 1.0f / tex.width, 1.0f / tex.height));
            ImageHistogramCS.SetBuffer(0, _HistogramDataIntId, cb);
            ImageHistogramCS.Dispatch(0, Mathf.CeilToInt((float)tex.width / (float)IHThreadNum), Mathf.CeilToInt((float)tex.height / (float)IHThreadNum), 1);
            // 数据收集
            cb.GetData(histogramBufferInt);
            cb.Release();
            // 归一化
            double[] histogramData = new double[histogramBufferCount];
            double n = 1.0d / (tex.width * tex.height);
            int index = 0;
            
            if (logout)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(string.Format("texture name = {0},histogramBuffer = ", tex.name));
                foreach (int intData in histogramBufferInt)
                {
                    double data = intData * n;
                    sb.Append(string.Format("\nindex = {0}, data = {1}", index, data));
                    histogramData[index] = data;
                    index++;
                }
                Debug.Log(sb);
            }
            else
            {
                foreach (int intData in histogramBufferInt)
                {
                    histogramData[index] = intData * n;
                    index++;
                }
            }
            
            return histogramData;
        }
        
        /// <summary>
        /// 计算直方图相似程度，超过0.8就可以基本判定为两张图相似
        /// </summary>
        /// <returns>相似程度值（0.0~1.0）</returns>
        public static double CalcSimilarity(double[] data1, double[] data2) {
            double[] mixedData = new double[data1.Length];
            for (int i = 0; i < data1.Length; i++) 
            {
                mixedData[i] = Math.Sqrt(data1[i] * data2[i]);
            }
            // The values of Bhattacharyya Coefficient ranges from 0 to 1,
            double similarity = 0;
            for (int i = 0; i < mixedData.Length; i++) 
            {
                similarity += mixedData[i];
            }
            // The degree of similarity
            return similarity;
        }
        public static double CalcSimilarity(List<double> data1, List<double> data2) {
            double[] mixedData = new double[data1.Count];
            for (int i = 0; i < data1.Count; i++) 
            {
                mixedData[i] = Math.Sqrt(data1[i] * data2[i]);
            }
            // The values of Bhattacharyya Coefficient ranges from 0 to 1,
            double similarity = 0;
            for (int i = 0; i < mixedData.Length; i++) 
            {
                similarity += mixedData[i];
            }
            // The degree of similarity
            return similarity;
        }
        #endregion
    }
}
