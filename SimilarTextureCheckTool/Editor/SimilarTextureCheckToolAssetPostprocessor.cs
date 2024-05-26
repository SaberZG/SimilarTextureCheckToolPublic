using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEditor;

namespace SimilarTextureCheckTool
{
    public class SimilarTextureCheckToolAssetPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// 需要修改的路径合集字典，path为资产相对路径
        /// int:0代表importedList，1代表deletedList，2代表movedList，3代表movedFromList，与OnPostprocessAllAssets的顺序一致
        /// 此举是为了避免每次修改到资产都需要读取庞大的SimilarTextureCacheAsset缓存，在图片对比界面增加新的更新缓存功能，用的就是这一份缓存
        /// 以路径作为键值，每个缓存记录都会记录下最终的状态
        /// </summary>
        private static Dictionary<string, int> totalModifyPathsDict = new Dictionary<string, int>();

        // 加载缓存数据
        private static void LoadCacheData()
        {
            totalModifyPathsDict.Clear();
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
                    // 转存字典，去重且方便查找
                    foreach (string path in cacheImportedList)
                    {
                        SetOrAddCache(path, 0);
                    }
                    foreach (string path in cacheDeletedList)
                    {
                        SetOrAddCache(path, 1);
                    }
                    foreach (string path in cacheMovedList)
                    {
                        SetOrAddCache(path, 2);
                    }
                    foreach (string path in cacheMovedFromList)
                    {
                        SetOrAddCache(path, 3);
                    }
                    EditorUtility.ClearProgressBar();
                }
                finally
                {
                    fs.Close();
                }
            }
        }

        private static void SetOrAddCache(string path, int val)
        {
            if (totalModifyPathsDict.ContainsKey(path))
            {
                totalModifyPathsDict[path] = val;
            }
            else
            {
                totalModifyPathsDict.Add(path, val);
            }
        }

        /// <summary>
        /// 监听资源变动，更新至本地缓存
        /// </summary>
        /// <param name="importedAssets">包含了所有被导入的资源的路径(导入资源，复制资源等行为)</param>
        /// <param name="deletedAssets">包含了所有被删除的资源的路径</param>
        /// <param name="movedAssets">包含了所有被移动的资源的新路径</param>
        /// <param name="movedFromAssetPaths">包含了所有被移动的资源的原始路径</param>
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            LoadCacheData();
            
            foreach (var path in importedAssets)
            {
                SetOrAddCache(path, 0);
                // Debug.LogFormat("importedAssets : {0}", path);
            }
            foreach (var path in deletedAssets)
            {
                SetOrAddCache(path, 1);
                // Debug.LogFormat("deletedAssets : {0}", path);
            }
            foreach (var path in movedAssets)
            {
                SetOrAddCache(path, 2);
                // Debug.LogFormat("movedAssets : {0}", path);
            }
            foreach (var path in movedFromAssetPaths)
            {
                SetOrAddCache(path, 3);
                // Debug.LogFormat("movedFromAssetPaths : {0}", path);
            }
            if (File.Exists(SimilarTextureCheckToolUtil.ModifyPathsSavePath))
                File.Delete(SimilarTextureCheckToolUtil.ModifyPathsSavePath);
            using (FileStream fs = File.OpenWrite(SimilarTextureCheckToolUtil.ModifyPathsSavePath))
            {
                BinaryFormatter bf = new BinaryFormatter();
                List<string> cacheImportedList = new List<string>();
                List<string> cacheDeletedList = new List<string>();
                List<string> cacheMovedList = new List<string>();
                List<string> cacheMovedFromList = new List<string>();
                foreach (var kv in totalModifyPathsDict)
                {
                    switch (kv.Value)
                    {
                        case 0:
                            cacheImportedList.Add(kv.Key);
                            break;
                        case 1:
                            cacheDeletedList.Add(kv.Key);
                            break;
                        case 2:
                            cacheMovedList.Add(kv.Key);
                            break;
                        case 3:
                            cacheMovedFromList.Add(kv.Key);
                            break;
                    }
                }
                bf.Serialize(fs, cacheImportedList);
                bf.Serialize(fs, cacheDeletedList);
                bf.Serialize(fs, cacheMovedList);
                bf.Serialize(fs, cacheMovedFromList);
            }
        }
    }
}