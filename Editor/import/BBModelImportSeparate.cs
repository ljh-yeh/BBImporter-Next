using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;
using System.Linq;



namespace BBImporter
{
    /// <summary>
    /// 导入模型时，分离对象
    /// </summary>
    public class BBModelImportSeparate : IBBMeshImporter
    {
        private readonly Vector2 resolution;
        private readonly bool filterHidden;
        private readonly string ignoreName;
        private readonly BBMeshParser meshParser;
        private readonly List<Material> materials;
        private readonly List<Vector2> textureResolutions;
        
        public BBModelImportSeparate(in Vector2 resolution, bool filterHidden, string ignoreName, List<Material> material, List<Vector2> textureResolutions = null)
        {
            this.resolution = resolution;
            this.filterHidden = filterHidden;
            this.ignoreName = ignoreName;
            this.materials = material;
            this.meshParser = new BBMeshParser(material, resolution, textureResolutions);
        }
        public void ParseOutline(AssetImportContext ctx, JObject file)
        {
            LoadGroupRecursively(file["outliner"], file, ctx);
        }
        private void LoadGroupRecursively(JToken currentGroup, JObject file,  AssetImportContext ctx)
        {
            foreach (var entry in currentGroup)
            {
                switch (entry.Type)
                {
                    case JTokenType.String:
                        var guid = entry.Value<string>();
                        var element = file["elements"].First(x => x.Value<string>("uuid") == guid);
                        if (element["visibility"]?.Value<bool>() == false && filterHidden) 
                            continue;
                        var mesh = new BBMeshParser(materials, resolution, textureResolutions);
                        var origin = element["origin"]?.Values<float>()?.ToArray().ReadVector3();
                        mesh.AddElement(element);
                        var goName = file["elements"].First(x => x.Value<string>("uuid") == entry.Value<string>()).Value<string>("name");
                        var newGO = mesh.BakeMesh(ctx, goName, guid, origin??Vector3.zero);
                        ctx.AddObjectToAsset(guid, newGO);
                        if (ctx.mainObject == null)
                            ctx.SetMainObject(newGO);
                        break;
                    case JTokenType.Object:
                        //TODO: Handle visible = false here
                        LoadGroupRecursively(entry["children"], file, ctx);
                        break;
                    default:
                        Debug.Log("Unhandled type " + entry.Type);
                        break;
                }
            }
        }
    }
}