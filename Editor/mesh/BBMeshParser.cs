using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BBImporter
{
    public class BBMeshParser
    {
        private readonly List<Material> materials;
        private readonly List<Vector2> textureSizes;
        private readonly List<BBVertex> vertices;
        private readonly Dictionary<int, List<int>> triangles;
        private readonly Vector2 resolution;
        private readonly List<Vector2> textureResolutions; // uv分辨率
        public bool IsEmpty => vertices.Count <= 0 || triangles.Count <= 0;

        public BBMeshParser(List<Material> materials, Vector2 resolution, List<Vector2> textureResolutions)
        {
            this.materials = materials;
            this.resolution = resolution;
            this.textureResolutions = textureResolutions ?? new List<Vector2>();

            textureSizes = new List<Vector2>();
            vertices = new List<BBVertex>();
            triangles = new Dictionary<int, List<int>>();
            foreach (var mat in materials)
            {
                if (mat.mainTexture != null)
                {
                    textureSizes.Add(new Vector2(mat.mainTexture.width, mat.mainTexture.height));
                }
            }
            if (textureSizes.Count <= 0)
            {
                textureSizes.Add(Vector2.one);
            }
        }

        public void AddElement(JToken element)
        {
            var type = element["type"];
            if (type == null || type.Value<string>() == "cube")
            {
                ParseCube(element);
            }
            else if (type.Value<string>() == "mesh")
            {
                ParseMesh(element);
            }
        }
        
        public GameObject BakeMesh(AssetImportContext ctx, string name, string guid, Vector3 origin)
        {
            var mesh = new Mesh();
            mesh.name = name = name.Replace("/", ".");
            mesh.vertices = vertices.Select(x => x.position - origin).ToArray();
            mesh.uv = vertices.Select(x => x.uv).ToArray();
            mesh.subMeshCount = triangles.Count;
            int count = 0;
            foreach (var submesh in triangles.OrderBy(x => x.Key))
            {
                mesh.SetTriangles(submesh.Value.ToArray(), count++);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            ctx.AddObjectToAsset(guid, mesh);
            GameObject go = new GameObject();
            go.name = name;
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = triangles
                .Where(x => x.Value.Count > 0)
                .OrderBy(x => x.Key)
                .Select(x => materials[x.Key]).ToArray();
            //ctx.AddObjectToAsset(name, go);
            return go;
        }
        private void ParseLocator(JToken element)
        {
            BBLocator bbLocator;
            try
            {
                bbLocator = element.ToObject<BBLocator>();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            var origin = bbLocator.position.ReadVector3();
            var rot = bbLocator.rotation.ReadQuaternion();
        }

        public void ParseCube(JToken element)
        {
            var bbCube = new BBModelCube(element);
            bbCube.GetMesh(vertices, triangles, textureResolutions);
        }
        
        public void ParseMesh(JToken element)
        {
            BBMesh bbMesh;
            try
            {
                bbMesh = element.ToObject<BBMesh>();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            var origin = bbMesh.origin.ReadVector3();
            var rot = bbMesh.rotation.ReadQuaternion();
            Matrix4x4 orientation = Matrix4x4.TRS(origin, rot, Vector3.one);
            var startPos = vertices.Count;
            //Fix visibility
            foreach (var faceEntry in bbMesh.faces)
            {
                if (faceEntry.Value.vertices.Length == 3)
                {
                    CreateMeshTriangle(bbMesh, faceEntry);
                }
                else if (faceEntry.Value.vertices.Length == 4)
                {
                    CreateMeshQuad3(bbMesh, faceEntry);
                }
                else
                {
                    //throw new NotImplementedException();
                    //this SHOULD be an error, but BlockBench sometimes has loose Edges, so lets just ignore them here
                    Debug.LogWarning($"Found loose edge in {faceEntry.Key}. Blockbench does that.");
                }
            }

            for (int i = startPos; i < vertices.Count; i++)
            {
                var before = vertices[i];
                var after = before.Transform(orientation);
                vertices[i] = after;
            }
        }

        private void CreateMeshTriangle(BBMesh bbMesh, KeyValuePair<string, BBMeshFace> faceEntry)
        {
            int materialNum = faceEntry.Value.texture;
            if (!triangles.TryGetValue(materialNum, out var triangleList))
            {
                triangleList = new List<int>();
                triangles[materialNum] = triangleList;
            }

            // 使用对应纹理的实际分辨率
            Vector2 actualResolution = resolution;
            if (textureResolutions.Count > materialNum)
            {
                actualResolution = textureResolutions[materialNum];
            }

            for (var i = 2; i >= 0; i--)
            {
                Vector3 pos = BBModelUtil.ReadVector3(bbMesh.vertices[faceEntry.Value.vertices[i]]);
                Vector2 uv = Vector2.zero;
                if (textureSizes.Count > materialNum)
                {
                    var texSize = textureSizes[materialNum];
                    uv = BBModelUtil.ReadVector2(faceEntry.Value.uv[faceEntry.Value.vertices[i]]) / actualResolution;
                    uv.y = 1 - uv.y;
                }
                var vert = new BBVertex(pos, uv);
                vertices.Add(vert);
                triangleList.Add(vertices.Count-1);
            }
        }

        private void CreateMeshQuad3(BBMesh bbMesh, KeyValuePair<string, BBMeshFace> faceEntry)
        {
            var aVtx = ReadVertex(bbMesh, faceEntry, 0);
            var bVtx = ReadVertex(bbMesh, faceEntry, 1);
            var cVtx = ReadVertex(bbMesh, faceEntry, 2);
            var dVtx = ReadVertex(bbMesh, faceEntry, 3);
            
            int materialNum = faceEntry.Value.texture;
            if (!triangles.TryGetValue(materialNum, out var triangleList))
            {
                triangleList = new List<int>();
                triangles[materialNum] = triangleList;
            }
            
            var a = aVtx.position;
            var b = bVtx.position;
            var c = cVtx.position;
            var d = dVtx.position;

            int startVertex = vertices.Count;
            
            if (IsDiagonal(a, b, c, d))
            {
                WriteTriangle(aVtx, bVtx, cVtx, dVtx, triangleList);
            }
            else if (IsDiagonal(a, c, b, d))
            {
                WriteTriangle(aVtx, cVtx, bVtx, dVtx, triangleList);
            }
            else
            {
                WriteTriangle(aVtx, dVtx, bVtx, cVtx, triangleList);
            }
        }

        private bool IsDiagonal(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            //var gizmo = GetImportGizmo();
            //gizmo.DrawLine(Vector3.zero, c, Color.red);
            //gizmo.DrawLine(a, b, Color.blue);
            var pointOnLine = GetPointOnLine(c, a, b);
            //gizmo.DrawLine(Vector3.zero, pointOnLine, Color.cyan);
            var plane = new Plane((c - pointOnLine).normalized, b);
            var distance = plane.GetDistanceToPoint(d);
            return distance < 0;
        }

        private bool IsCoplanar(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var cDot = Vector3.Dot(c - a, c - b);
            var dDot = Vector3.Dot(d - a, d - b);
            return (cDot > 0 && dDot > 0) || (cDot <= 0 && dDot <= 0);
        }
        
        private BBVertex ReadVertex(BBMesh bbMesh, KeyValuePair<string, BBMeshFace> faceEntry, int index)
        {
            int materialNum = faceEntry.Value.texture;
            Vector3 pos = BBModelUtil.ReadVector3(bbMesh.vertices[faceEntry.Value.vertices[index]]);
            Vector2 uv = Vector2.zero;

            // 使用对应纹理的实际分辨率
            Vector2 actualResolution = resolution;
            if (textureResolutions.Count > materialNum)
            {
                actualResolution = textureResolutions[materialNum];
            }

            if (textureSizes.Count > materialNum)
            {
                var texSize = textureSizes[materialNum];
                uv = BBModelUtil.ReadVector2(faceEntry.Value.uv[faceEntry.Value.vertices[index]]) / actualResolution;
                uv.y = 1 - uv.y;
            }
            return new BBVertex(pos, uv);
        }
        
        private void WriteTriangle(BBVertex aVtx, BBVertex bVtx, BBVertex cVtx, BBVertex dVtx, List<int> triangleList)
        {
            int startVertex = vertices.Count;
            vertices.Add(cVtx);
            vertices.Add(aVtx);
            vertices.Add(bVtx);

            var a = aVtx.position;
            var b = bVtx.position;
            var c = cVtx.position;
            var d = dVtx.position;

            if (IsCoplanar(a, b, c, d))
            {
                vertices.Add(dVtx);
                vertices.Add(bVtx);
                vertices.Add(aVtx);
            }
            else
            {
                vertices.Add(dVtx);
                vertices.Add(bVtx);
                vertices.Add(aVtx);
            }
            for (int i = 0; i < 6; i++)
            {
                triangleList.Add(startVertex + i);
            }
        }

        Vector3 GetPointOnLine(Vector3 p, Vector3 a, Vector3 b)
        {
            return a + Vector3.Project(p - a, b - a);
        }
    }

    static class FloatArrayExtension
    {
        public static Vector3 ReadVector3(this float[] arr)
        {
            return BBModelUtil.ReadVector3(arr);
        }
        public static Quaternion ReadQuaternion(this float[] arr)
        {
            return BBModelUtil.ReadQuaternion(arr);
        }
    }
}