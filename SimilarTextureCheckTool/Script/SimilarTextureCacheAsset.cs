using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SimilarTextureCheckTool
{
    public class SimilarTextureCacheAsset
    {
        // 编辑下使用的数据，方便读取存储
        public Dictionary<string, SimilarTextureCacheData> cacheSimilarDict = new Dictionary<string, SimilarTextureCacheData>();
        // 最终存储下来的列表数据
        public List<SimilarTextureCacheData> savedCacheDataList = new List<SimilarTextureCacheData>();

    #if UNITY_EDITOR
        public void Init()
        {
            cacheSimilarDict.Clear();
            foreach (var similarTextureCacheData in savedCacheDataList)
            {
                cacheSimilarDict.Add(similarTextureCacheData.texturePath, similarTextureCacheData);
            }
        }
        public bool CanSkipTextureCalculation(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            return CanSkipTextureCalculation(path);
        }

        public bool CanSkipTextureCalculation(string path)
        {
            return !string.Empty.Equals(path) && IsContainsData(path);
        }

        private bool IsContainsData(string path)
        {
            return cacheSimilarDict.ContainsKey(path);
        }

        public void AppendCacheDataFormTempDic(Dictionary<string, SimilarTextureCacheData> tempDict)
        {
            foreach (var kvPair in tempDict)
            {
                AppendCacheData(kvPair.Value);
            }

            RecollectDataIntoSaveList();
        }
        
        /// <summary>
        /// 插入单个缓存数据，但最终需要手动调度一次 RecollectDataIntoSaveList 
        /// </summary>
        /// <param name="data"></param>
        public void AppendCacheData(SimilarTextureCacheData data)
        {
            if (!IsContainsData(data.texturePath))
            {
                cacheSimilarDict.Add(data.texturePath, data);
            }
            else
            {
                // 先允许替换
                cacheSimilarDict[data.texturePath] = data;
            }
        }
        public void RecollectDataIntoSaveList()
        {
            savedCacheDataList.Clear();
            foreach (var kvPair in cacheSimilarDict)
            {
                savedCacheDataList.Add(kvPair.Value);
            }
        }

        public SimilarTextureCacheData TryGetCacheData(string path)
        {
            if (IsContainsData(path))
            {
                return cacheSimilarDict[path];
            }

            return null;
        }
        
        public void TryRemoveCacheData(string path)
        {
            if (IsContainsData(path))
            {
                cacheSimilarDict.Remove(path);
            }
        }
        
    #endif
    }
    [Serializable]
    public class SimilarTextureCacheData
    {
        public string texturePath;
        public string perceptualHash;
        public List<double> ImageHistogramData;
    }
}
