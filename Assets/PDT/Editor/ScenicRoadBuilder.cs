using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using TMPro;
using Object = UnityEngine.Object;

public static class ScenicRoadBuilder
{
    public const string Folder = "Assets/PDT/Environment";
    public const string PrefabPath = Folder + "/PDT Scenic Road.prefab";
    public const string ScenePath = "Assets/PDT/Scenes/DrivingTestV2.unity";
    private static ScenicRoadSettings settings;
    private static readonly List<Vector3> path = new List<Vector3>();
    private static readonly List<float> distances = new List<float>();
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    private static GameObject root;
    private static float length;

    [MenuItem("PDT/Environment/Rebuild Scenic Road Assets")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        settings = AssetDatabase.LoadAssetAtPath<ScenicRoadSettings>(Folder + "/Road Settings.asset");
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ScenicRoadSettings>();
            AssetDatabase.CreateAsset(settings, Folder + "/Road Settings.asset");
        }
        if (settings.controlPoints == null || settings.controlPoints.Length < 4)
            throw new InvalidOperationException("The closed road needs at least four control points.");
        SamplePath();
        materials.Clear();
        Mat("Asphalt", new Color(.23f, .27f, .28f), 7f);
        Mat("Gravel", new Color(.56f, .52f, .40f), 9f);
        Mat("Meadow", new Color(.34f, .46f, .22f), 3f);
        Mat("Edge paint", new Color(.90f, .89f, .74f));
        Mat("Centre paint", new Color(.94f, .67f, .24f));
        Mat("Steel", new Color(.48f, .55f, .56f));
        Mat("Timber", new Color(.24f, .18f, .12f));
        Mat("Pine", new Color(.16f, .31f, .23f));
        Mat("Sage", new Color(.27f, .40f, .25f));
        Mat("Rock", new Color(.44f, .46f, .39f), 6f);
        Mat("Sign", new Color(.10f, .22f, .23f));
        root = new GameObject("PDT Scenic Road");
        try
        {
            BuildRoad();
            BuildTerrain();
            BuildScenery();
            BuildFurniture();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Scenic road baked: {length:F0} m, pavement {settings.roadWidth:F1} m, shoulders {settings.shoulderWidth:F1} m.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static void BuildBatch()
    {
        Build();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Install(scene);
        EditorSceneManager.SaveScene(scene);
        WriteMetrics();
    }

    [MenuItem("PDT/Environment/Install In Open DrivingTestV2")]
    public static void InstallOpenScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open DrivingTestV2 first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build();
        Install(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void Install(Scene scene)
    {
        foreach (var item in scene.GetRootGameObjects())
        {
            if (item.name == "Ground")
            {
                Undo.RecordObject(item, "Disable prototype ground");
                item.SetActive(false);
            }
            if (item.name == "PDT Scenic Road") Undo.DestroyObjectImmediate(item);
        }
        var environment = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
        Undo.RegisterCreatedObjectUndo(environment, "Install scenic road");
        // Environment-only lighting; no camera or volume components are modified.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.64f, .74f, .81f);
        RenderSettings.ambientEquatorColor = new Color(.47f, .52f, .43f);
        RenderSettings.ambientGroundColor = new Color(.24f, .28f, .20f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.65f, .75f, .80f);
        RenderSettings.fogStartDistance = 240f;
        RenderSettings.fogEndDistance = 600f;
    }

    private static void SamplePath()
    {
        path.Clear(); distances.Clear(); length = 0;
        Vector3[] p = settings.controlPoints;
        int count = p.Length;
        for (int i = 0; i < count; i++)
        for (int j = 0; j < settings.samplesPerSpan; j++)
        {
            float t = j / (float)settings.samplesPerSpan;
            Vector3 a = p[(i + count - 1) % count], b = p[i], c = p[(i + 1) % count], d = p[(i + 2) % count];
            Vector3 point = .5f * ((2f * b) + (-a + c) * t + (2*a - 5*b + 4*c - d) * t*t + (-a + 3*b - 3*c + d) * t*t*t);
            point.y *= settings.elevationScale;
            if (path.Count > 0) length += Vector3.Distance(path[path.Count - 1], point);
            path.Add(point); distances.Add(length);
        }
        length += Vector3.Distance(path[path.Count - 1], path[0]);
        path.Add(path[0]); distances.Add(length);
    }

    private static Vector3 Right(int i)
    {
        int n = path.Count - 1;
        Vector3 tangent = path[(i + 1) % n] - path[(i + n - 1) % n];
        return new Vector3(tangent.z, 0, -tangent.x).normalized;
    }

    private static float Bay(Vector3 p) => p.x < 12f ? 7f * Mathf.SmoothStep(0, 1, 1f - Mathf.Abs(p.z) / 32f) : 0f;

    private static void BuildRoad()
    {
        var surface = new Geo(2);
        var edges = new Geo(); var centre = new Geo();
        float half = settings.roadWidth * .5f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i], right = Right(i);
            float[] offsets = { -half - settings.shoulderWidth, -half, half + Bay(p), half + Bay(p) + settings.shoulderWidth };
            foreach (float offset in offsets) surface.Vertex(p + right * offset, new Vector2(offset / 5f, distances[i] / 5f));
            if (i == 0) continue;
            for (int col = 0; col < 3; col++) surface.QuadIndices((i-1)*4+col, i*4+col, i*4+col+1, (i-1)*4+col+1, col == 1 ? 0 : 1);
            Strip(edges, i, -half + .25f, .13f);
            // The outside line follows the widened arrival bay.
            Strip(edges, i, half - .25f, .13f, true);
            if (distances[i] % 12f < 4f && !(path[i].x < 12f && Mathf.Abs(path[i].z) < 34f))
                Strip(centre, i, 0f, .13f);
        }
        MeshObject("Road and flush shoulders", surface, new[] { materials["Asphalt"], materials["Gravel"] }, true);
        MeshObject("Ivory road edges", edges, new[] { materials["Edge paint"] });
        MeshObject("Broken amber centre line", centre, new[] { materials["Centre paint"] });
    }

    private static void Strip(Geo geo, int i, float offset, float width, bool bay = false)
    {
        Vector3 a = path[i-1] + Vector3.up * .015f, b = path[i] + Vector3.up * .015f;
        float oa = offset + (bay ? Bay(a) : 0), ob = offset + (bay ? Bay(b) : 0);
        geo.Quad(a + Right(i-1)*(oa-width/2), b + Right(i)*(ob-width/2), b + Right(i)*(ob+width/2), a + Right(i-1)*(oa+width/2));
    }

    private static float Nearest(Vector3 position, out Vector3 roadPoint)
    {
        float best = float.MaxValue; roadPoint = Vector3.zero;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i], delta = path[i+1] - a;
            Vector2 d = new Vector2(delta.x, delta.z), q = new Vector2(position.x-a.x, position.z-a.z);
            float t = Mathf.Clamp01(Vector2.Dot(q, d) / Mathf.Max(.001f, d.sqrMagnitude));
            float sq = (q - d*t).sqrMagnitude;
            if (sq < best) { best = sq; roadPoint = Vector3.Lerp(a, path[i+1], t); }
        }
        return Mathf.Sqrt(best);
    }

    private static float Height(float x, float z)
    {
        float distance = Nearest(new Vector3(x,0,z), out Vector3 roadPoint);
        float clear = settings.roadWidth/2 + settings.shoulderWidth + Bay(roadPoint) + 4f;
        float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distance-clear)/32f));
        float outer = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distance-35f)/65f));
        float hills = 1.5f + 2f*Mathf.PerlinNoise(x*.016f+40f,z*.016f+40f)
            + settings.backgroundHillHeight*outer*(.35f+.65f*Mathf.PerlinNoise(x*.009f+70,z*.009f+70));
        return Mathf.Lerp(roadPoint.y - .09f, hills, blend);
    }

    private static void BuildTerrain()
    {
        const int nx = 125, nz = 130;
        var terrain = new Geo();
        for (int iz=0; iz<=nz; iz++) for (int ix=0; ix<=nx; ix++)
        {
            float x=-140+ix*4, z=-260+iz*4;
            terrain.Vertex(new Vector3(x,Height(x,z),z), new Vector2(x/40f,z/40f));
            if (ix>0 && iz>0)
            {
                int k=iz*(nx+1)+ix;
                terrain.QuadIndices(k-nx-2,k-1,k,k-nx-1);
            }
        }
        MeshObject("Rolling meadow - collision terrain", terrain, new[] { materials["Meadow"] }, true);
    }

    private static void BuildScenery()
    {
        var random = new System.Random(settings.scenerySeed);
        var trunks = new Geo(); var pine = new Geo(); var sage = new Geo(); var rocks = new Geo();
        var colliders = new GameObject("Scenery colliders"); colliders.transform.SetParent(root.transform, false);
        int planted=0;
        for (int attempt=0; planted<settings.treeCount && attempt<settings.treeCount*35; attempt++)
        {
            float x=-100+(float)random.NextDouble()*420, z=-210+(float)random.NextDouble()*410;
            float distance=Nearest(new Vector3(x,0,z),out Vector3 roadPoint);
            if (distance < settings.roadWidth/2+settings.shoulderWidth+Bay(roadPoint)+10f) continue;
            if (Mathf.PerlinNoise(x*.018f+30,z*.018f+30)<.42f) continue;
            float h=6+(float)random.NextDouble()*7;
            Vector3 position=new Vector3(x,Height(x,z),z);
            trunks.Cone(position, .25f, .19f, h*.65f, 7);
            Geo foliage=planted%3==0 ? sage : pine;
            foliage.Cone(position+Vector3.up*h*.22f,h*.25f,0,h*.63f,8);
            foliage.Cone(position+Vector3.up*h*.47f,h*.19f,0,h*.53f,8);
            var trunk=new GameObject("Tree trunk"); trunk.transform.SetParent(colliders.transform,false); trunk.transform.position=position;
            var c=trunk.AddComponent<CapsuleCollider>(); c.radius=.3f; c.height=h*.65f; c.center=Vector3.up*c.height/2;
            planted++;
        }
        for (int i=0;i<settings.rockCount;i++)
        {
            float x=-65+(float)random.NextDouble()*360, z=-180+(float)random.NextDouble()*355;
            if (Nearest(new Vector3(x,0,z),out Vector3 roadPoint)<settings.roadWidth/2+settings.shoulderWidth+Bay(roadPoint)+7) continue;
            float size=1+(float)random.NextDouble()*2.8f;
            Vector3 p=new Vector3(x,Height(x,z)-.1f,z);
            rocks.Cone(p,size,.38f*size,size*.85f,6);
            var stone=new GameObject("Rock collider"); stone.transform.SetParent(colliders.transform,false); stone.transform.position=p;
            var c=stone.AddComponent<BoxCollider>(); c.center=Vector3.up*size*.36f; c.size=new Vector3(size*1.3f,size*.72f,size*1.3f);
        }
        MeshObject("Pine groves",pine,new[]{materials["Pine"]});
        MeshObject("Sage groves",sage,new[]{materials["Sage"]});
        MeshObject("Tree trunks",trunks,new[]{materials["Timber"]});
        MeshObject("Meadow rocks",rocks,new[]{materials["Rock"]});
    }

    private static void BuildFurniture()
    {
        var steel=new Geo(); var timber=new Geo(); var reflectors=new Geo();
        var railRoot=new GameObject("Guardrails - outer hillside"); railRoot.transform.SetParent(root.transform,false);
        float lastPost=-10;
        for (int i=1;i<path.Count;i++)
        {
            if (path[i].y<2.9f) continue;
            float offset=settings.roadWidth/2+settings.shoulderWidth+.35f;
            Vector3 a=path[i-1]-Right(i-1)*offset, b=path[i]-Right(i)*offset;
            Vector3 mid=(a+b)*.5f, direction=b-a;
            Quaternion rotation=Quaternion.LookRotation(direction);
            steel.Box(mid+Vector3.up*.68f,new Vector3(.18f,.3f,direction.magnitude+.04f),rotation);
            var segment=new GameObject("Rail collider"); segment.transform.SetParent(railRoot.transform,false);
            segment.transform.SetPositionAndRotation(mid+Vector3.up*.43f,rotation);
            var box=segment.AddComponent<BoxCollider>(); box.size=new Vector3(.22f,.86f,direction.magnitude+.04f);
            if (distances[i]-lastPost>5)
            {
                timber.Box(b+Vector3.up*.40f,new Vector3(.18f,.8f,.18f),Quaternion.identity);
                reflectors.Box(b+Right(i)*.13f+Vector3.up*.7f,new Vector3(.06f,.10f,.20f),rotation);
                lastPost=distances[i];
            }
        }
        for (int i=0;i<path.Count-1;i+=14)
        {
            if (Mathf.Abs(path[i].z)<35 && path[i].x<12) continue;
            Vector3 p=path[i]+Right(i)*(settings.roadWidth/2+settings.shoulderWidth+.5f);
            reflectors.Box(p+Vector3.up*.45f,new Vector3(.13f,.9f,.13f),Quaternion.identity);
        }
        MeshObject("Galvanized hillside rails",steel,new[]{materials["Steel"]});
        MeshObject("Guardrail supports",timber,new[]{materials["Timber"]});
        MeshObject("Roadside delineators",reflectors,new[]{materials["Edge paint"]});
        Sign("PDT MEADOW ROAD",new Vector3(17,Height(17,-20),-20),0,"PDT\nMEADOW ROAD");
        Sign("Scenic road sign",new Vector3(-10,Height(-10,45),45),0,"SCENIC LOOP\nKEEP RIGHT");
        Sign("Arrival sign",new Vector3(18,Height(18,13),13),90,"PRIVATE ROAD\nENJOY THE DRIVE");
        // Two simple benches in the arrival clearing, safely outside the shoulder.
        var benches=new Geo();
        for(int i=0;i<2;i++)
        {
            Vector3 p=new Vector3(21,Height(21,-7+i*9),-7+i*9);
            benches.Box(p+Vector3.up*.5f,new Vector3(.65f,.12f,2.2f),Quaternion.identity);
            benches.Box(p+new Vector3(.3f,.9f,0),new Vector3(.1f,.8f,2.2f),Quaternion.identity);
            for(int k=-1;k<=1;k+=2) benches.Box(p+new Vector3(0,.24f,k*.75f),new Vector3(.45f,.48f,.15f),Quaternion.identity);
        }
        MeshObject("Arrival benches",benches,new[]{materials["Timber"]},true);
    }

    private static void Sign(string name,Vector3 p,float yaw,string text)
    {
        var sign=new GameObject(name); sign.transform.SetParent(root.transform,false); sign.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
        var board=GameObject.CreatePrimitive(PrimitiveType.Cube); board.name="Sign board"; board.transform.SetParent(sign.transform,false);
        board.transform.localPosition=new Vector3(0,2,0); board.transform.localScale=new Vector3(3.6f,1.3f,.15f); board.GetComponent<Renderer>().sharedMaterial=materials["Sign"];
        for(int side=-1;side<=1;side+=2)
        {
            var post=GameObject.CreatePrimitive(PrimitiveType.Cube); post.name="Timber post"; post.transform.SetParent(sign.transform,false);
            post.transform.localPosition=new Vector3(side*1.3f,.8f,0); post.transform.localScale=new Vector3(.15f,1.6f,.15f); post.GetComponent<Renderer>().sharedMaterial=materials["Timber"];
        }
        var label=new GameObject("Road sign lettering"); label.transform.SetParent(sign.transform,false); label.transform.localPosition=new Vector3(0,2,-.09f);
        var mesh=label.AddComponent<TextMeshPro>(); mesh.text=text; mesh.alignment=TextAlignmentOptions.Center;
        mesh.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        mesh.fontSize=4f; mesh.color=new Color(.95f,.91f,.73f);mesh.rectTransform.sizeDelta=new Vector2(3.35f,1.1f);
    }

    private static Material Mat(string name,Color color,float noise=0)
    {
        string file=Folder+"/"+name+".mat";
        Material mat=AssetDatabase.LoadAssetAtPath<Material>(file);
        if(mat==null) { mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,file); }
        mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",.08f);
        if(noise>0)
        {
            var tex=new Texture2D(128,128,TextureFormat.RGB24,true); tex.name=name+" grain"; tex.wrapMode=TextureWrapMode.Repeat;
            var colors=new Color[128*128]; var random=new System.Random(80);
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float v=.77f+.18f*Mathf.PerlinNoise(x/noise,y/noise)+(float)random.NextDouble()*.05f;
                colors[y*128+x]=new Color(v,v,v);
            }
            tex.SetPixels(colors);tex.Apply();
            Texture2D saved=SaveAsset(tex,Folder+"/"+name+" grain.asset"); mat.SetTexture("_BaseMap",saved);
        }
        EditorUtility.SetDirty(mat); materials[name]=mat; return mat;
    }

    private static T SaveAsset<T>(T source,string file) where T:Object
    {
        T existing=AssetDatabase.LoadAssetAtPath<T>(file);
        if(existing==null) { AssetDatabase.CreateAsset(source,file);return source; }
        EditorUtility.CopySerialized(source,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(source);return existing;
    }

    private static void MeshObject(string name,Geo geo,Material[] mats,bool collision=false)
    {
        if(geo.vertices.Count==0)return;
        Mesh mesh=geo.Mesh(name); mesh=SaveAsset(mesh,Folder+"/"+name+".asset");
        var obj=new GameObject(name); obj.transform.SetParent(root.transform,false);
        obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterials=mats;
        if(collision)obj.AddComponent<MeshCollider>().sharedMesh=mesh;
    }

    private static void WriteMetrics()
    {
        float maxGrade=0,minRadius=float.MaxValue;
        for(int i=1;i<path.Count-1;i++)
        {
            Vector3 a=path[i]-path[i-1],b=path[i+1]-path[i];
            maxGrade=Mathf.Max(maxGrade,Mathf.Abs(a.y)/new Vector2(a.x,a.z).magnitude);
            a.y=0;b.y=0;float angle=Vector3.Angle(a,b)*Mathf.Deg2Rad;
            if(angle>.0001f)minRadius=Mathf.Min(minRadius,(a.magnitude+b.magnitude)*.5f/angle);
        }
        var car=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PDT/Prefabs/DreamMobileCar.prefab"));
        Bounds bounds=new Bounds();bool first=true;
        foreach(var renderer in car.GetComponentsInChildren<Renderer>())if(renderer.enabled){if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
        Object.DestroyImmediate(car);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Environment-metrics.txt",$"Loop: {length:F1} m\nRoad width: {settings.roadWidth} m\nShoulders: {settings.shoulderWidth} m each\nMaximum grade: {maxGrade*100:F1}%\nMinimum centreline bend radius: {minRadius:F1} m\nVisual car bounds: {bounds.size}\nCasual loop at 8-12 m/s: {length/12:F0}-{length/8:F0} seconds\n");
    }

    private sealed class Geo
    {
        public readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int>[] triangles;
        public Geo(int submeshes=1){triangles=new List<int>[submeshes];for(int i=0;i<submeshes;i++)triangles[i]=new List<int>();}
        public void Vertex(Vector3 p,Vector2 tex){vertices.Add(p);uv.Add(tex);}
        public void QuadIndices(int a,int b,int c,int d,int sub=0){triangles[sub].AddRange(new[]{a,b,c,a,c,d});}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int k=vertices.Count;Vertex(a,Vector2.zero);Vertex(b,Vector2.up);Vertex(c,Vector2.one);Vertex(d,Vector2.right);QuadIndices(k,k+1,k+2,k+3);}
        public void Box(Vector3 p,Vector3 size,Quaternion q)
        {
            Vector3[] v=new Vector3[8];for(int i=0;i<8;i++)v[i]=p+q*Vector3.Scale(size,new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f));
            Quad(v[0],v[2],v[3],v[1]);Quad(v[5],v[7],v[6],v[4]);Quad(v[4],v[6],v[2],v[0]);Quad(v[1],v[3],v[7],v[5]);Quad(v[2],v[6],v[7],v[3]);Quad(v[4],v[0],v[1],v[5]);
        }
        public void Cone(Vector3 p,float bottom,float top,float height,int sides)
        {
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                Vector3 u=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),v=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                Quad(p+u*bottom,p+u*top+Vector3.up*height,p+v*top+Vector3.up*height,p+v*bottom);
                if(top>0)Quad(p+Vector3.up*height,p+v*top+Vector3.up*height,p+u*top+Vector3.up*height,p+Vector3.up*height);
            }
        }
        public Mesh Mesh(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
