using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Rendering;

public class Login : MonoBehaviour {
	public GridClient Client = new GridClient();
	public Dictionary<uint, FacetedMesh> newPrims = new Dictionary<uint, FacetedMesh>();
	public Dictionary<uint, GameObject> objects = new Dictionary<uint, GameObject>();
	MeshmerizerR R = new MeshmerizerR();

	// Use this for initialization
	string userName = "Test User";
	string password = "1234";
	string server = "http://localhost:9000";
	
	bool terrainModified = false;
	float[,] heightTable = new float[256, 256];
	GameObject terrain;
	GameObject terrainPart1;
	GameObject terrainPart2;
	UnityEngine.Mesh mesh1;
	UnityEngine.Mesh mesh2;
	UnityEngine.Material m;
	const float MinimumTimeBetweenTerrainUpdated = 1f;	//in second
	float terrainTimeSinceUpdate = MinimumTimeBetweenTerrainUpdated + 1f;
	bool terrainTextureNeedsUpdate = false;
	System.Drawing.Bitmap terrainImage = null;
	
	void Start () { 
		m = new UnityEngine.Material(Shader.Find ("Diffuse"));
		
		terrain = new GameObject("terrain");
		terrain.transform.position = new UnityEngine.Vector3(0, 0, 0);
		
		terrainPart1 = new GameObject("part1");
		terrainPart1.transform.parent = terrain.transform;
		terrainPart1.transform.localPosition = new UnityEngine.Vector3(0, 0, 0);
		terrainPart1.AddComponent("MeshFilter");
		(terrainPart1.AddComponent("MeshRenderer") as MeshRenderer).material = m;
		mesh1 = (terrainPart1.GetComponent<MeshFilter>() as MeshFilter).mesh;
		

		terrainPart2 = new GameObject("part2");
		terrainPart2.transform.parent = terrain.transform;
		terrainPart2.transform.localPosition = new UnityEngine.Vector3(0, 0, 0);
		terrainPart2.AddComponent("MeshFilter");
		(terrainPart2.AddComponent("MeshRenderer") as MeshRenderer).material = m;
		mesh2 = (terrainPart2.GetComponent<MeshFilter>() as MeshFilter).mesh;
		
		Client.Settings.STORE_LAND_PATCHES = true;
		
	}
	
	Texture2D Bitmap2Texture2D(System.Drawing.Bitmap bitmap)
	{
		byte[] byteArray = null;
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            byteArray = stream.ToArray();
        }
		Texture2D tex = new Texture2D(bitmap.Width, bitmap.Height);
		tex.LoadImage(byteArray);
		return tex;
	}
	
	void ProcessPrim(FacetedMesh mesh)
	{
		GameObject obj = new GameObject(mesh.Prim.LocalID.ToString());
		GameObject parent = null;
		if (mesh.Prim.ParentID != 0)
		{
			if (!objects.ContainsKey(mesh.Prim.ParentID))
				ProcessPrim(newPrims[mesh.Prim.ParentID]);
			parent = objects[mesh.Prim.ParentID];
			obj.transform.parent = parent.transform;
		}
		
		// Create vertices, uv, triangles for EACH FACE that stores the 3D data in Unity3D friendly format
		for (int j = 0; j < mesh.Faces.Count; j++)
	    {
			Face face = mesh.Faces[j];
			GameObject faceObj = new GameObject("face" + j.ToString());
			faceObj.transform.parent = obj.transform;
			(faceObj.AddComponent("MeshRenderer") as MeshRenderer).material = m;
			UnityEngine.Mesh unityMesh = (faceObj.AddComponent("MeshFilter") as MeshFilter).mesh;
			
			MakeMesh(unityMesh, face, 0, face.Vertices.Count - 1, 0, face.Indices.Count - 1, 0);
		}			
		obj.transform.localPosition = new UnityEngine.Vector3(mesh.Prim.Position.X, mesh.Prim.Position.Y, -mesh.Prim.Position.Z);
		obj.transform.localRotation = new UnityEngine.Quaternion(mesh.Prim.Rotation.X, mesh.Prim.Rotation.Y, -mesh.Prim.Rotation.Z, mesh.Prim.Rotation.W);
		obj.transform.localScale = new UnityEngine.Vector3(mesh.Prim.Scale.X, mesh.Prim.Scale.Y, mesh.Prim.Scale.Z);
		objects[mesh.Prim.LocalID] = obj;
	}
	
	// Update is called once per frame
	void Update () {
		//Update prims
		foreach (var item in newPrims)
		{			
			ProcessPrim(item.Value);
		}
		newPrims.Clear();
		
		//Update terrain
		terrainTimeSinceUpdate += Time.deltaTime;
		if (terrainModified && terrainTimeSinceUpdate > MinimumTimeBetweenTerrainUpdated)
		{
			UpdateTerrain();
		}			
		
		if (terrainTextureNeedsUpdate)
        	UpdateTerrainTexture();
	}
	
	void UpdateTerrainTexture()
	{
		Simulator sim = Client.Network.CurrentSim;
        terrainImage = Radegast.Rendering.TerrainSplat.Splat(Client, heightTable,
            new UUID[] { sim.TerrainDetail0, sim.TerrainDetail1, sim.TerrainDetail2, sim.TerrainDetail3 },
            new float[] { sim.TerrainStartHeight00, sim.TerrainStartHeight01, sim.TerrainStartHeight10, sim.TerrainStartHeight11 },
            new float[] { sim.TerrainHeightRange00, sim.TerrainHeightRange01, sim.TerrainHeightRange10, sim.TerrainHeightRange11 });
		
		Texture2D tex = Bitmap2Texture2D(terrainImage);
		terrainPart1.GetComponent<MeshRenderer>().material.mainTexture = tex;
		terrainPart2.GetComponent<MeshRenderer>().material.mainTexture = tex;
		
        terrainTextureNeedsUpdate = false;
	}
	
	void MakeMesh(UnityEngine.Mesh mesh, Face face, int vertices_begin, int vertices_end, int indices_begin, int indices_end, int indices_offset)
	{		
		//we have to clear the mesh first, otherwise there will be exceptions.
		mesh.Clear();
		
		int vertices_count = vertices_end - vertices_begin + 1;
		UnityEngine.Vector3[] vertices = new UnityEngine.Vector3[vertices_count];
		UnityEngine.Vector3[] normals = new UnityEngine.Vector3[vertices_count];
		UnityEngine.Vector2[] uv = new UnityEngine.Vector2[vertices_count];
		for (int k = vertices_begin, i = 0; k <= vertices_end; ++k, ++i)
		{
			vertices[i].x = face.Vertices[k].Position.X;
			vertices[i].y = face.Vertices[k].Position.Y;
			//HACK: unity3d uses left-hand coordinate, so we have to mirror z corrd
			vertices[i].z = -face.Vertices[k].Position.Z;
			
			normals[i].x = face.Vertices[k].Normal.X;
			normals[i].y = face.Vertices[k].Normal.Y;
			//HACK: unity3d uses left-hand coordinate, so we have to mirror z corrd
			normals[i].z = -face.Vertices[k].Normal.Z;
			
			uv[i].x = face.Vertices[k].TexCoord.X;
			//HACK: unity3d uses left-bottom corner as the origin of the texture
			uv[i].y = 1 - face.Vertices[k].TexCoord.Y;
		}
		//indices for this face
		int[] triangles = new int[indices_end - indices_begin + 1];
		for (int k = indices_begin, i = 0; k <= indices_end; k += 3, i += 3)
		{
			//HACK: OpenGL's default front face is counter-clock-wise
			triangles[i] = face.Indices[k + 2] - indices_offset;
			triangles[i + 1] = face.Indices[k + 1] - indices_offset;
			triangles[i + 2] = face.Indices[k + 0] - indices_offset;
		}
		
		mesh.vertices = vertices;
		mesh.normals = normals;
		mesh.uv = uv;
		mesh.triangles = triangles;
	}
	
	void UpdateTerrain()
    {
		terrainModified = false;
		
        if (Client.Network.CurrentSim == null || Client.Network.CurrentSim.Terrain == null) return;
		Debug.Log("UpdateTerrain");
        int step = 1;

        for (int x = 0; x < 256; x += step)
        {
            for (int y = 0; y < 256; y += step)
            {
                float z = 0;
                int patchNr = ((int)x / 16) * 16 + (int)y / 16;
                if (Client.Network.CurrentSim.Terrain[patchNr] != null
                    && Client.Network.CurrentSim.Terrain[patchNr].Data != null)
                {
                    float[] data = Client.Network.CurrentSim.Terrain[patchNr].Data;
                    z = data[(int)x % 16 * 16 + (int)y % 16];
                }
                heightTable[x, y] = z;	
            }
        }

        Face face = R.TerrainMesh(heightTable, 0f, 255f, 0f, 255f);
		
		//Unity doesn't allow a mesh with over 65000 vertices while face have 65536 vertices.
		//We need to split face mesh into 2 peices.
		//mesh1: vertices 0~32768+255=33023, normals 0~33023, uv 0~33023, indices 0~255*2*128*3-1=195839
		//mesh2: vertices 32768~65535, normals 32768~65535, uv 32768~65535, indices 195840~390149
		
		MakeMesh(mesh1, face, 0, 33023, 0, 195839, 0);
		MakeMesh(mesh2, face, 32768, 65535, 195840, 390149, 32768);
		
        terrainTimeSinceUpdate = 0f;
        if (terrainModified)Debug.Log("terrainModified set to true by other thread!");
		terrainTextureNeedsUpdate = true;
    }
	
	void StartLogin(string name, string pass, string server)
	{
		string LOGIN_SERVER = server;
        string FIRST_NAME = name.Split(' ')[0];
        string LAST_NAME = name.Split(' ')[1];
        string PASSWORD = pass;
        string CHANNEL = "KZLAGENT";
        string VERSION = "KZLVERSION";

        LoginParams loginParams = Client.Network.DefaultLoginParams(
            FIRST_NAME, LAST_NAME, PASSWORD, CHANNEL, VERSION);

        loginParams.URI = LOGIN_SERVER; // KZL

        // Set handler
        Client.Network.LoginProgress += Network_OnLoginProcess;
//            Client.Network.Disconnected += Network_OnDisconnected;
//            Client.Network.LoggedOut += Network_OnLoggedout;
//
        Client.Objects.ObjectUpdate += Objects_OnObjectUpdate;
		Client.Terrain.LandPatchReceived += Terrain_LandPatchReceived;
        // Login to Simulator
        Client.Network.BeginLogin(loginParams);
		Debug.Log("Start Connecting");
	}
	
	void Network_OnLoginProcess(object sender, LoginProgressEventArgs e)
    {
        if (e.Status == LoginStatus.ConnectingToSim)
        { // first time
			Debug.Log("Connecting to OpenSim");
        }
        else if (e.Status == LoginStatus.Success)
        { // second time
            Debug.Log("Connecting Success!");
        }
        else if (e.Status == LoginStatus.Failed)
        {
            Debug.Log("Connecting Failed!");
        }
    }
	
	void LogOut()
	{
		if (Client.Network.Connected)
		{
			Client.Network.Logout();
			Debug.Log ("Logout success!");
		}
	}
	
    void Objects_OnObjectUpdate(object sender, PrimEventArgs e)
    {
		FacetedMesh mesh = null;
		
		//FIXME : need to lock prims
		if (objects.ContainsKey(e.Prim.LocalID))
		{
			Debug.Log ("recieve prim with LocalID " + e.Prim.LocalID.ToString() + " again!");
			return;
		}
				
		if (e.Prim.Sculpt != null)
		{
			//leave sculpt prim out temporarily
		}
		else
		{
			mesh = R.GenerateFacetedMesh(e.Prim, DetailLevel.Highest);
			newPrims[e.Prim.LocalID] = mesh;
		}
	}
	
	void Terrain_LandPatchReceived(object sender, LandPatchReceivedEventArgs e)
	{
		Debug.Log ("LandPatchReceive");
		terrainModified = true;
	}
	
	void OnGUI()
	{
		GUILayout.Label("用户名");
		userName = GUILayout.TextField(userName);
		GUILayout.Label("密码");
		password = GUILayout.TextField(password);
		GUILayout.Label("登录地址");
		server = GUILayout.TextField(server);
		if (GUILayout.Button("登录"))
			StartLogin(userName, password, server);
		if (GUILayout.Button("登出"))
			LogOut();
	}
}
