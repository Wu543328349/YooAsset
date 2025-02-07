﻿using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YooAsset.Editor
{
	[TaskAttribute("验证构建结果")]
	public class TaskVerifyBuildResult : IBuildTask
	{
		void IBuildTask.Run(BuildContext context)
		{
			var buildParametersContext = context.GetContextObject<BuildParametersContext>();
			
			// 模拟构建模式下跳过验证
			if (buildParametersContext.Parameters.BuildMode == EBuildMode.SimulateBuild)
				return;

			// 验证构建结果
			if (buildParametersContext.Parameters.VerifyBuildingResult)
			{
				var unityManifestContext = context.GetContextObject<TaskBuilding.UnityManifestContext>();
				VerifyingBuildingResult(context, unityManifestContext.UnityManifest);
			}
		}

		/// <summary>
		/// 验证构建结果
		/// </summary>
		private void VerifyingBuildingResult(BuildContext context, AssetBundleManifest unityManifest)
		{
			var buildParameters = context.GetContextObject<BuildParametersContext>();
			var buildMapContext = context.GetContextObject<BuildMapContext>();
			string[] buildedBundles = unityManifest.GetAllAssetBundles();

			// 1. 过滤掉原生Bundle
			string[] expectBundles = buildMapContext.BundleInfos.Where(t => t.IsRawFile == false).Select(t => t.BundleName).ToArray();

			// 2. 验证Bundle
			List<string> intersectBundleList = buildedBundles.Except(expectBundles).ToList();
			if (intersectBundleList.Count > 0)
			{
				foreach (var intersectBundle in intersectBundleList)
				{
					Debug.LogWarning($"差异资源包: {intersectBundle}");
				}
				throw new System.Exception("存在差异资源包！请查看警告信息！");
			}

			// 3. 验证Asset
			bool isPass = true;
			var buildMode = buildParameters.Parameters.BuildMode;
			if (buildMode == EBuildMode.ForceRebuild || buildMode == EBuildMode.IncrementalBuild)
			{
				int progressValue = 0;
				foreach (var buildedBundle in buildedBundles)
				{
					string filePath = $"{buildParameters.PipelineOutputDirectory}/{buildedBundle}";
					string[] allBuildinAssetPaths = GetAssetBundleAllAssets(filePath);
					string[] expectBuildinAssetPaths = buildMapContext.GetBuildinAssetPaths(buildedBundle);
					if (expectBuildinAssetPaths.Length != allBuildinAssetPaths.Length)
					{
						Debug.LogWarning($"构建的Bundle文件内的资源对象数量和预期不匹配 : {buildedBundle}");
						isPass = false;
						continue;
					}

					foreach (var buildinAssetPath in allBuildinAssetPaths)
					{
						var guid = AssetDatabase.AssetPathToGUID(buildinAssetPath);
						if (string.IsNullOrEmpty(guid))
						{
							Debug.LogWarning($"无效的资源路径，请检查路径是否带有特殊符号或中文：{buildinAssetPath}");
							isPass = false;
							continue;
						}

						bool isMatch = false;
						foreach (var exceptBuildAssetPath in expectBuildinAssetPaths)
						{
							var guidExcept = AssetDatabase.AssetPathToGUID(exceptBuildAssetPath);
							if (guid == guidExcept)
							{
								isMatch = true;
								break;
							}
						}
						if (isMatch == false)
						{
							Debug.LogWarning($"在构建的Bundle文件里发现了没有匹配的资源对象：{buildinAssetPath}");
							isPass = false;
							continue;
						}
					}

					EditorTools.DisplayProgressBar("验证构建结果", ++progressValue, buildedBundles.Length);
				}
				EditorTools.ClearProgressBar();
				if (isPass == false)
				{
					throw new Exception("构建结果验证没有通过，请参考警告日志！");
				}
			}

			BuildRunner.Log("构建结果验证成功！");
		}

		/// <summary>
		/// 解析.manifest文件并获取资源列表
		/// </summary>
		private string[] GetAssetBundleAllAssets(string filePath)
		{
			string manifestFilePath = $"{filePath}.manifest";
			List<string> assetLines = new List<string>();
			using (StreamReader reader = File.OpenText(manifestFilePath))
			{
				string content;
				bool findTarget = false;
				while (null != (content = reader.ReadLine()))
				{
					if (content.StartsWith("Dependencies:"))
						break;
					if (findTarget == false && content.StartsWith("Assets:"))
						findTarget = true;
					if (findTarget)
					{
						if (content.StartsWith("- "))
						{
							string assetPath = content.TrimStart("- ".ToCharArray());
							assetLines.Add(assetPath);
						}
					}
				}
			}
			return assetLines.ToArray();
		}
	}
}