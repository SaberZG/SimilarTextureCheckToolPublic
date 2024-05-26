![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/23f472d7-bbed-4393-a1fa-0735708c89fd)# SimilarTextureCheckToolPublic
一个用于检测项目内相似图片，并提供替换/删除的工具

这个工具是基于我的一些项目情况编写的工具，旨在使用公共的文件夹存放图片/图集等资源，减少项目中的重复图片
# 简单的使用介绍
- 代码有个地方需要自行确定具体的工具路径
  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/0f7d617a-b016-49c3-a282-9875050f94fd)

- 在这里进入窗口
  
  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/10404334-22f9-416a-b83c-354502315d34)

- 界面面板和一些注意事项如下
  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/35bd133f-48a8-451b-9bdd-e8c74564f2f9)
  进入面板时会自动更新资产引用情况，或者手动更新，推荐每次打开面板时都点击一次更新，保证替换时不会出现遗漏问题

- 搜索遍历面板效果如下
  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/33fbad2e-74c2-47ee-82ee-8f7f8f9bc9c3)

  根据显示设置和图像特征值参数，以及是否限定了遍历目录，找到相似的或可能相似的图片，并列出来

- 点击批量处理后的操作面板

  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/a10b707e-2b68-402d-9d2c-d566cc7ade40)

  可选择设置一个公共的图片文件夹，并将当前查看的图片拷贝一份到公共图片文件夹内

  设置了替换原图片后，方可激活替换和删除逻辑的按钮执行相应操作
  
- 点击单个图片单独处理的操作面板

  ![image](https://github.com/SaberZG/SimilarTextureCheckToolPublic/assets/74618371/104ba9d1-4ffe-42e9-9d09-36b624ae84a3)

  与批量处理的界面相似

界面做了很多优化处理，例如修改缓存优化，界面刷新优化，避免了大量现存图片过多导致的缓存更新问题，和界面大量图片的加载卡顿问题

开发版本为Unity 2021.3.7，旧版本可以运行，但有部分API需要更新，如果有问题的话可以联系我>_<
