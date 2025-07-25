using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;
using System.Linq;

namespace BBImporter
{
    /// <summary>
    /// 导入模型时，保持层级和动画
    /// </summary>
    public class BBModelImportHierarchy
    {
        private readonly Vector2 resolution;
        private readonly bool filterHidden;
        private readonly string ignoreName;
        private readonly List<Material> materials;
        private readonly List<Vector2> textureResolutions;
        private Dictionary<string, GameObject> groups;

        public BBModelImportHierarchy(in Vector2 resolution, bool filterHidden, string ignoreName, List<Material> materials, List<Vector2> textureResolutions = null)
        {
            this.resolution = resolution;
            this.filterHidden = filterHidden;
            this.ignoreName = ignoreName;
            this.materials = materials;
            this.textureResolutions = textureResolutions;
            this.groups = new Dictionary<string, GameObject>();
        }

        public void ParseOutline(AssetImportContext ctx, JObject file)
        {
            var guid = file["model_identifier"]?.Value<string>();
            var nameOfObject = file["name"].Value<string>();
            if (string.IsNullOrEmpty(guid))
                guid = nameOfObject;
            var rootGO = new GameObject();
            ctx.AddObjectToAsset(guid, rootGO);
            ctx.SetMainObject(rootGO);
            LoadGroupRecursively(file["outliner"], rootGO, Vector3.zero, ctx, file);
            LoadAnimations(ctx, file);
        }

        private void LoadGroupRecursively(JToken currentGroup, GameObject parent, Vector3 parentOrigin, AssetImportContext ctx, JObject file)
        {
            foreach (var outline in currentGroup)
            {
                switch (outline.Type)
                {
                    case JTokenType.String:
                        var guid = outline.Value<string>();
                        var element = file["elements"].First(x => x.Value<string>("uuid") == guid);
                        if (element["visibility"]?.Value<bool>() == false && filterHidden)
                            return;
                        string type = element["type"]?.Value<string>();
                        switch (type)
                        {
                            case null:
                            case "cube":
                            case "mesh":
                                LoadMesh(file, outline, element, parent, parentOrigin, ctx, guid);
                                break;
                            case "locator":
                                LoadLocator(file, outline, element, parent, parentOrigin);
                                break;
                            default:
                                Debug.LogWarning($"Unsupported type {element["type"].Value<string>()}");
                                break;
                        }

                        break;
                    case JTokenType.Object:
                        var outliner = outline.ToObject<BBOutliner>();
                        // 给bone添加后缀
                        var boneGO = new GameObject(outliner.name + "-Group");
                        // 需要使用相对坐标，且坐标值的计算，全程使用bbmodel的值，不能用parent.transform.localPosition（两个坐标是不同的坐标系，不能直接用于计算）
                        var origin = outliner.origin.ReadVector3();
                        boneGO.transform.localPosition = origin - parentOrigin;
                        boneGO.transform.rotation = outliner.rotation.ReadQuaternion();
                        boneGO.transform.SetParent(parent.transform, false);
                        groups.Add(outliner.uuid, boneGO);
                        LoadGroupRecursively(outline["children"], boneGO, origin, ctx, file);
                        break;
                    default:
                        Debug.Log("Unhandled type " + outline.Type);
                        break;
                }
            }
        }

        private void LoadLocator(JObject file, JToken outline, JToken element, GameObject parent, Vector3 parentOrigin)
        {
            var goName = file["elements"].First(x => x.Value<string>("uuid") == outline.Value<string>()).Value<string>("name");
            goName += ("-Locator");
            var origin = element["position"]?.Values<float>()?.ToArray().ReadVector3();
            var rotation = element["rotation"]?.Values<float>()?.ToArray().ReadQuaternion();
            var go = new GameObject(goName);
            // 需要使用相对坐标，且坐标值的计算，全程使用bbmodel的值，不能用parent.transform.localPosition（两个坐标是不同的坐标系，不能直接用于计算）
            go.transform.localPosition = (origin ?? Vector3.zero) - parentOrigin;
            if (rotation != null)
                go.transform.rotation = rotation.Value;
            go.transform.SetParent(parent.transform, false);
        }

        private void LoadMesh(JObject file, JToken outline, JToken element, GameObject parent, Vector3 parentOrigin, AssetImportContext ctx, string guid)
        {
            var mesh = new BBMeshParser(materials, resolution, textureResolutions);
            mesh.AddElement(element);
            var origin = element["origin"]?.Values<float>()?.ToArray().ReadVector3() ?? Vector3.zero;
            var goName = file["elements"].First(x => x.Value<string>("uuid") == outline.Value<string>()).Value<string>("name");
            var go = mesh.BakeMesh(ctx, goName, guid, origin);
            // 需要使用相对坐标，且坐标值的计算，全程使用bbmodel的值，不能用parent.transform.localPosition（两个坐标是不同的坐标系，不能直接用于计算）
            go.transform.localPosition = origin - parentOrigin;
            bool isMesh = element["type"]?.Value<string>() == "mesh";
            var rotation = element["rotation"]?.Values<float>()?.ToArray().ReadQuaternion(isMesh);
            if (rotation != null)
                go.transform.rotation = rotation.Value;
            go.transform.SetParent(parent.transform, false);
        }

        private void LoadAnimations(AssetImportContext ctx, JObject obj)
        {
            var animToken = obj["animations"];
            if (animToken is { HasValues: true })
            {
                var mainGO = ctx.mainObject as GameObject;
                Animation animation = null;
                if (mainGO != null)
                    animation = mainGO.AddComponent<Animation>();
                foreach (var token in obj["animations"])
                {
                    var anim = token.ToObject<BBAnimation>();
                    var clip = anim.ToClip(this.groups);
                    ctx.AddObjectToAsset(anim.name, clip);
                    if (animation != null)
                        animation.AddClip(clip, clip.name);
                }
            }
        }
    }
}