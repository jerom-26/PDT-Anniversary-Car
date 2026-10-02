// Run in the disposable Unity copy only; not part of the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class EnvironmentValidation
{
    private const string Pending="PDT.EnvironmentChecks";
    private static readonly List<string> report=new List<string>();
    private static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static EnvironmentValidation()
    {
        EditorApplication.update+=()=>
        {
            if(EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);Run();}
        };
    }
    public static void RunBatch()
    {
        if(!Application.isBatchMode)throw new Exception("Use a disposable batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    public static void ValidateInstalledSceneBatch()
    {
        if(!Application.isBatchMode)throw new Exception("Use a disposable batch project.");
        Scene scene=EditorSceneManager.OpenScene(ScenicRoadBuilder.ScenePath,OpenSceneMode.Single);
        GameObject environment=null,ground=null,spawn=null;int environmentCount=0,cameras=0;
        foreach(GameObject root in scene.GetRootGameObjects())
        {
            if(root.name=="PDT Scenic Road"){environment=root;environmentCount++;}
            if(root.name=="Ground")ground=root;
            if(root.name=="VehicleSpawnPoint")spawn=root;
            cameras+=root.GetComponentsInChildren<Camera>(true).Length;
            foreach(Transform child in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject)==0,
                    "No missing scripts on "+child.name);
        }
        Check(environmentCount==1 && environment!=null,"Installed scene contains exactly one scenic road environment");
        Check(ground!=null && !ground.activeSelf,"Original prototype Ground is inactive");
        Check(spawn!=null && Vector3.Distance(spawn.transform.position,Vector3.up)<.001f,
            "Existing spawn position remains unchanged");
        Check(cameras==1,"Existing scene retains its single camera");
        int meshes=0;
        foreach(MeshFilter filter in environment.GetComponentsInChildren<MeshFilter>(true))
        {
            Check(filter.sharedMesh!=null,"Baked mesh reference resolves: "+filter.name);meshes++;
        }
        foreach(Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            foreach(Material material in renderer.sharedMaterials)
                Check(material!=null && material.shader!=null && material.shader.name!="Hidden/InternalErrorShader",
                    "Material and shader resolve: "+renderer.name);
        foreach(MeshCollider collider in environment.GetComponentsInChildren<MeshCollider>(true))
            Check(collider.sharedMesh!=null,"Collision mesh resolves: "+collider.name);
        var settings=AssetDatabase.LoadAssetAtPath<ScenicRoadSettings>(ScenicRoadBuilder.Folder+"/Road Settings.asset");
        Check(settings!=null && settings.controlPoints.Length==11,"Road authoring settings resolve correctly");
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Environment-installed-scene.txt",
            $"PASS: Installed DrivingTestV2 scene loaded successfully.\nPASS: No missing scene scripts.\nPASS: {meshes} baked mesh references and all materials/shaders/collision meshes resolve.\nPASS: One scenic environment; old Ground disabled.\nPASS: Existing spawn position and single camera retained.\nPASS: Road settings asset resolves.\n");
        Debug.Log("Installed environment scene validation passed.");
    }
    private static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);report.Add("PASS: "+message);}

    private static void Run()
    {
        int exit=0;
        try
        {
            Scene scene=SceneManager.CreateScene("Scenic road physics",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            PhysicsScene physics=scene.GetPhysicsScene();
            var environment=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ScenicRoadBuilder.PrefabPath));
            SceneManager.MoveGameObjectToScene(environment,scene);
            var road=environment.transform.Find("Road and flush shoulders").GetComponent<MeshCollider>();
            var verts=road.sharedMesh.vertices;int count=verts.Length/4-1;
            var points=new Vector3[count];var rights=new Vector3[count];
            for(int i=0;i<count;i++)
            {
                rights[i]=(verts[i*4+2]-verts[i*4+1]).normalized;
                points[i]=verts[i*4+1]+rights[i]*5f;
            }
            Physics.SyncTransforms();
            Check(Vector3.Distance(verts[0],verts[count*4])<.00001f,"Closed road mesh has matching seam vertices");
            float maxGrade=0;
            for(int i=0;i<count;i++)
            {
                Vector3 delta=points[(i+1)%count]-points[i];
                maxGrade=Mathf.Max(maxGrade,Mathf.Abs(delta.y)/new Vector2(delta.x,delta.z).magnitude);
                foreach(float offset in new[]{-6.8f,-4f,0f,4f,6.8f})
                {
                    // Bay widens only the positive side; regular widths remain drivable.
                    Vector3 p=points[i]+rights[i]*offset;
                    CheckRay(physics,p,road);
                }
            }
            Check(maxGrade<.04f,"Road grades remain below 4% with no jump ramps");
            report.Add($"PASS: {count*5} road/shoulder collision probes have no gaps or terrain intrusions");
            Check(physics.Raycast(new Vector3(0,5,0),Vector3.down,out RaycastHit spawnHit,10)
                && spawnHit.collider==road && Mathf.Abs(spawnHit.point.y)<.02f,"Original spawn point lands on level paved arrival area");

            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PDT/Prefabs/DreamMobileCar.prefab");
            var car=Object.Instantiate(prefab,Vector3.up,Quaternion.identity);SceneManager.MoveGameObjectToScene(car,scene);
            var controller=car.GetComponent<PlayerCarController>();var body=car.GetComponent<Rigidbody>();
            var tick=typeof(PlayerCarController).GetMethod("FixedUpdate",Flags);
            var move=typeof(PlayerCarController).GetField("moveInput",Flags);
            var turn=typeof(PlayerCarController).GetField("turnInput",Flags);
            for(int i=0;i<100;i++){tick.Invoke(controller,null);physics.Simulate(.02f);}
            Check(Mathf.Abs(body.position.y-.2f)<.06f,"Unmodified car settles correctly at existing spawn");
            int nearest=48,progress=0;float maxOffset=0,maxVerticalSpeed=0;
            for(int frame=0;frame<6500 && progress<count;frame++)
            {
                int advance=0;float best=float.MaxValue;
                for(int j=0;j<12;j++)
                {
                    float sq=(points[(nearest+j)%count]-body.position).sqrMagnitude;
                    if(sq<best){best=sq;advance=j;}
                }
                nearest=(nearest+advance)%count;progress+=advance;
                int target=nearest;float lookahead=0;
                while(lookahead<11f){lookahead+=Vector3.Distance(points[target],points[(target+1)%count]);target=(target+1)%count;}
                Vector3 to=points[target]-body.position;to.y=0;
                float angle=Vector3.SignedAngle(body.rotation*Vector3.forward,to,Vector3.up);
                float speed=Vector3.Dot(body.linearVelocity,body.rotation*Vector3.forward);
                move.SetValue(controller,speed<10f?1f:0f);turn.SetValue(controller,Mathf.Clamp(angle/24f,-1,1));
                tick.Invoke(controller,null);physics.Simulate(.02f);
                float lateral=Mathf.Abs(Vector3.Dot(body.position-points[nearest],rights[nearest]));
                maxOffset=Mathf.Max(maxOffset,lateral);maxVerticalSpeed=Mathf.Max(maxVerticalSpeed,Mathf.Abs(body.linearVelocity.y));
                if(lateral>4.8f || body.position.y<points[nearest].y-.1f || body.position.y>points[nearest].y+.65f)
                    throw new Exception($"Road drive left stable paved corridor at sample {nearest}: lateral={lateral:F2}, body={body.position}, road={points[nearest]}");
            }
            Check(progress>=count,$"Unmodified controller completed the whole loop at approximately 10 m/s; max lateral deviation {maxOffset:F2} m");
            Check(maxVerticalSpeed<1.5f,$"Gentle elevation caused no launches; maximum vertical speed {maxVerticalSpeed:F2} m/s");
            int hill=0;for(int i=0;i<count;i++)if(points[i].y>points[hill].y)hill=i;
            controller.ResetVehicle();body.position=points[hill]-rights[hill]*4f+Vector3.up*.26f;
            body.rotation=Quaternion.LookRotation(-rights[hill]);body.linearVelocity=-rights[hill]*8f;
            move.SetValue(controller,0f);turn.SetValue(controller,0f);Physics.SyncTransforms();
            for(int i=0;i<50;i++){tick.Invoke(controller,null);physics.Simulate(.02f);}
            float outward=-Vector3.Dot(body.position-points[hill],rights[hill]);
            Check(outward<7f && body.position.y>points[hill].y-.1f,$"Guardrail stopped an 8 m/s outward impact without tunnelling (offset {outward:F2} m)");
            int identity=car.GetInstanceID();Transform cameraTarget=car.transform.Find("CameraTarget");
            controller.ResetVehicle();
            Check(car.GetInstanceID()==identity && car.transform.Find("CameraTarget")==cameraTarget && Vector3.Distance(body.position,Vector3.up)<.001f,
                "Phase 1 reset preserves the same car and camera target at the original spawn");
            body.position=new Vector3(700,-20,700);tick.Invoke(controller,null);
            Check(Vector3.Distance(body.position,Vector3.up)<.001f,"Off-map fall recovery still returns to the existing spawn");
            report.Add("PASS: Environment validation completed with the existing controller unchanged.");
        }
        catch(Exception e){report.Add("FAIL: "+e);exit=1;}
        finally{Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/Environment-results.txt",report);Debug.Log(string.Join("\n",report));EditorApplication.Exit(exit);}
    }
    private static void CheckRay(PhysicsScene physics,Vector3 p,Collider road)
    {
        if(!physics.Raycast(p+Vector3.up*8,Vector3.down,out RaycastHit hit,12) || hit.collider!=road || Mathf.Abs(hit.point.y-p.y)>.045f)
            throw new Exception($"Road surface probe failed at {p}: hit {hit.collider?.name}, height {hit.point.y}");
    }

    public static void RenderBatch()
    {
        ShaderUtil.allowAsyncCompilation=false;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ScenicRoadBuilder.PrefabPath));
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.64f,.74f,.81f);RenderSettings.ambientEquatorColor=new Color(.47f,.52f,.43f);RenderSettings.ambientGroundColor=new Color(.24f,.28f,.2f);
        var sun=new GameObject("Preview sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.7f;sun.transform.rotation=Quaternion.Euler(42,-32,0);sun.shadows=LightShadows.Soft;
        var camera=new GameObject("Temporary preview camera").AddComponent<Camera>();camera.backgroundColor=new Color(.65f,.75f,.8f);camera.clearFlags=CameraClearFlags.SolidColor;camera.farClipPlane=1000;camera.fieldOfView=52;
        Capture(camera,new Vector3(-180,280,-290),new Vector3(90,0,0),"Environment-overview.png");
        var car=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PDT/Prefabs/DreamMobileCar.prefab"),new Vector3(0,.23f,0),Quaternion.identity);
        Capture(camera,new Vector3(-10,6,-18),new Vector3(2,1,18),"Environment-arrival.png");
        Capture(camera,new Vector3(210,12,90),new Vector3(231,3,10),"Environment-hillside.png");
        Object.DestroyImmediate(car);
    }
    private static void Capture(Camera camera,Vector3 position,Vector3 target,string file)
    {
        camera.transform.position=position;camera.transform.LookAt(target);
        var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
        Directory.CreateDirectory("Logs");File.WriteAllBytes("Logs/"+file,image.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);
    }
}
