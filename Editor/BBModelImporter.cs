using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BBImporter
{
    [ScriptedImporter(1, "bbmodel")]
    public class BBModelImporter : ScriptedImporter
    {
        [SerializeField] private Material materialTemplate;
        [SerializeField] private MeshImportMode importMode;
        [SerializeField] private bool filterHidden;
        [SerializeField] private string ignoreName;

        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Smoothness = Shader.PropertyToID("_Glossiness");
        // 纹理的uv分辨率
        private List<Vector2> textureResolutions = new List<Vector2>();
        private Vector2 Resolution;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            //var basePath = Path.GetDirectoryName(ctx.assetPath) + "/" + Path.GetFileNameWithoutExtension(ctx.assetPath);
            string file = File.ReadAllText(ctx.assetPath);
            var obj = JObject.Parse(file);
            var materials = LoadMaterials(ctx, obj);
            Resolution = LoadResolution(obj);

            switch (importMode)
            {
                case MeshImportMode.MergeAllIntoOneObject:
                    {
                        var importer = new BBModelImportMerged(Resolution, filterHidden, ignoreName, materials, textureResolutions);
                        importer.ParseOutline(ctx, obj);
                        break;
                    }
                case MeshImportMode.SeparateObjects:
                    {
                        var importer = new BBModelImportSeparate(Resolution, filterHidden, ignoreName, materials, textureResolutions);
                        importer.ParseOutline(ctx, obj);
                        break;
                    }
                case MeshImportMode.WithHierarchyAndAnimations:
                    {
                        var importer = new BBModelImportHierarchy(Resolution, filterHidden, ignoreName, materials, textureResolutions);
                        importer.ParseOutline(ctx, obj);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private Vector3 LoadResolution(JObject obj)
        {
            return new Vector3(obj["resolution"]["width"].Value<float>(), obj["resolution"]["height"].Value<float>());
        }

        private List<Material> LoadMaterials(AssetImportContext ctx, JObject obj)
        {
            var ret = new List<Material>();
            if (!obj["textures"].HasValues)
            {
                if (materialTemplate == null)
                {
                    // 默认材质改为urp材质
                    materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    materialTemplate ??= new Material(Shader.Find("Standard"));
                    materialTemplate.SetFloat(Metallic, 0f);
                    materialTemplate.SetFloat(Smoothness, 0f);
                }
                Material mat = Instantiate(materialTemplate);
                ret.Add(mat);
                ctx.AddObjectToAsset(mat.name, mat);
                return ret;
            }
            
            // 添加纹理分辨率列表
            var textureResolutions = new List<Vector2>();
            
            foreach (var token in obj["textures"])
            {
                var texture = token.ToObject<BBTexture>();
                if (materialTemplate == null)
                {
                    // 默认材质改为urp材质
                    materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    materialTemplate ??= new Material(Shader.Find("Standard"));
                    materialTemplate.SetFloat(Metallic, 0f);
                    materialTemplate.SetFloat(Smoothness, 0f);
                }
                Material mat = Instantiate(materialTemplate);

                Vector2 textureResolution = Resolution; // 默认使用全局分辨率
                
                if (texture != null)
                {
                    string[] texData = texture.source.Split(',');
                    Debug.Assert(texData[0] == "data:image/png;base64");
                    Texture2D tex = new(2, 2)
                    {
                        filterMode = FilterMode.Point
                    };
                    var texBytes = Convert.FromBase64String(texData[1]);
                    tex.LoadImage(texBytes);
                    mat.mainTexture = tex;
                    tex.name = texture.name;
                    ctx.AddObjectToAsset(texture.uuid, tex);
                    
                    // 优先使用纹理的uv_width和uv_height；如果没有则使用全局分辨率
                    if (token["uv_width"] != null && token["uv_height"] != null)
                    {
                        textureResolution = new Vector2(
                            token["uv_width"].Value<float>(),
                            token["uv_height"].Value<float>()
                        );
                    }
                }

                textureResolutions.Add(textureResolution);

                mat.name = token["name"].Value<string>();
                var guid = token["uuid"].Value<string>();
                ctx.AddObjectToAsset(guid, mat);
                ret.Add(mat);
            }

            // 保存纹理分辨率信息
            SetTextureResolutions(textureResolutions);

            return ret;
        }
        
        // 设置纹理的uv分辨率
        private void SetTextureResolutions(List<Vector2> resolutions)
        {
            textureResolutions = resolutions;
        }
    }

    public enum MeshImportMode
    {
        [InspectorName("保持层级和动画")]
        [Tooltip("维护原始的骨骼层级结构并支持完整的关键帧动画系统")]
        WithHierarchyAndAnimations,

        [InspectorName("合并为单个对象")]
        [Tooltip("将所有几何体合并为一个Mesh对象，适用于静态装饰物和性能优化场景")]
        MergeAllIntoOneObject,

        [InspectorName("分离对象（有BUG）")]
        [Tooltip("每个元素创建独立的GameObject，便于单独控制各个部分")]
        SeparateObjects,
    }
}