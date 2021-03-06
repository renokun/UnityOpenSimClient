using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using OpenMetaverse.Imaging;
using System.IO;
using System.Drawing;
using System;

public class Login : MonoBehaviour {
	public static GridClient Client = new GridClient();
	public Dictionary<uint, FacetedMesh> newPrims = new Dictionary<uint, FacetedMesh>();
	public Dictionary<uint, GameObject> objects = new Dictionary<uint, GameObject>();
	
	public Dictionary<uint, Radegast.Rendering.RenderAvatar> newAvatars = new Dictionary<uint, Radegast.Rendering.RenderAvatar>();
	public Dictionary<uint, GameObject> avatars = new Dictionary<uint, GameObject>();
	public Dictionary<uint, Radegast.Rendering.RenderAvatar> renderAvatars = new Dictionary<uint, Radegast.Rendering.RenderAvatar>();
	public List<uint> avHasTex = new List<uint>();
	//public Dictionary<uint, UUID> faceID2TexUUID = new Dictionary<uint, UUID>();
	
	public Dictionary<UUID, Texture2D> textures = new Dictionary<UUID, Texture2D>();
	public Dictionary<UUID, Bitmap> bitmaps = new Dictionary<UUID, Bitmap>();
	MeshmerizerR R = new MeshmerizerR();
	
	public GameObject primObjects;
	public GameObject avatarObjects;
	
	// Use this for initialization
	string userName = "qiang cao";
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
	
	List<TerseObjectUpdateEventArgs> newTerseAvatarUpdates = new List<TerseObjectUpdateEventArgs>();
	
	void Start () { 
		m = new UnityEngine.Material(Shader.Find ("Diffuse"));
		
		terrain = new GameObject("terrain");
		terrain.transform.position = new UnityEngine.Vector3(0, 0, 0);
		
		terrainPart1 = new GameObject("part1");
		terrainPart1.transform.parent = terrain.transform;
		terrainPart1.transform.localPosition = new UnityEngine.Vector3(0, 0, 0);
		terrainPart1.AddComponent("MeshFilter");
		terrainPart1.AddComponent("MeshRenderer");
		mesh1 = (terrainPart1.GetComponent<MeshFilter>() as MeshFilter).mesh;
		

		terrainPart2 = new GameObject("part2");
		terrainPart2.transform.parent = terrain.transform;
		terrainPart2.transform.localPosition = new UnityEngine.Vector3(0, 0, 0);
		terrainPart2.AddComponent("MeshFilter");
		terrainPart2.AddComponent("MeshRenderer");
		mesh2 = (terrainPart2.GetComponent<MeshFilter>() as MeshFilter).mesh;
		
		Client.Settings.STORE_LAND_PATCHES = true;
		
		primObjects = new GameObject("prims");
		avatarObjects = new GameObject("avatars");
		
		// Set handler
        Client.Network.LoginProgress += Network_OnLoginProcess;
//            Client.Network.Disconnected += Network_OnDisconnected;
//            Client.Network.LoggedOut += Network_OnLoggedout;
//
        Client.Objects.ObjectUpdate += Objects_OnObjectUpdate;
		Client.Objects.AvatarUpdate += Objects_AvatarUpdate;
		Client.Avatars.AvatarAppearance += Avatars_AvatarAppearance;
		Client.Appearance.AppearanceSet += Appearance_AppearanceSet;
		Client.Objects.TerseObjectUpdate += Objects_TerseObjectUpdate;
		Client.Terrain.LandPatchReceived += Terrain_LandPatchReceived;
		Radegast.Rendering.GLAvatar.loadlindenmeshes2("avatar_lad.xml");
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
		if (objects.ContainsKey(mesh.Prim.LocalID))
			return;
			
		GameObject obj = new GameObject(mesh.Prim.LocalID.ToString());
		
		GameObject parent = null;
		if (mesh.Prim.ParentID != 0)
		{
			if (!objects.ContainsKey(mesh.Prim.ParentID))
			{
				if (newPrims.ContainsKey(mesh.Prim.ParentID) == false)
					Debug.Break();
				ProcessPrim(newPrims[mesh.Prim.ParentID]);	//it seems that the parent is received before children
			}
			parent = objects[mesh.Prim.ParentID];			
		}
		else
			parent = primObjects;
		
		// Create vertices, uv, triangles for EACH FACE that stores the 3D data in Unity3D friendly format
		for (int j = 0; j < mesh.Faces.Count; j++)
	    {
			Face face = mesh.Faces[j];
			GameObject faceObj = new GameObject("face" + j.ToString());
			faceObj.transform.parent = obj.transform;
			UnityEngine.Material mat = (faceObj.AddComponent("MeshRenderer") as MeshRenderer).material;
			
			if (textures.ContainsKey(face.TextureFace.TextureID))
				mat.mainTexture = textures[face.TextureFace.TextureID];
			else if (bitmaps.ContainsKey(face.TextureFace.TextureID))
			{
				Texture2D tex = Bitmap2Texture2D(bitmaps[face.TextureFace.TextureID]);
				tex.wrapMode = TextureWrapMode.Repeat;
				textures[face.TextureFace.TextureID] = tex;
				mat.mainTexture = tex;
				bitmaps.Remove(face.TextureFace.TextureID);
			}
			else
				mat = m;
			
			UnityEngine.Mesh unityMesh = (faceObj.AddComponent("MeshFilter") as MeshFilter).mesh;
			
			MakeMesh(unityMesh, face, 0, face.Vertices.Count - 1, 0, face.Indices.Count - 1, 0);
		}			
		//second life's child object's position and rotation is local, but scale are global. 
		//So we have to set parent when setting position, and unset parent when setting rotation and scale.
		//Radegast explains well:
		//pos = parentPos + obj.InterpolatedPosition * parentRot;
        //rot = parentRot * obj.InterpolatedRotation;
		obj.transform.position = parent.transform.position + 
			parent.transform.rotation * new UnityEngine.Vector3(mesh.Prim.Position.X, mesh.Prim.Position.Y, -mesh.Prim.Position.Z);
				
		//we invert the z axis, and Second Life rotatation is about right hand, but Unity rotation is about left hand, so we negate the x and y part of the quaternion. 
		//You have to deeply understand the quaternion to understand this.
		obj.transform.rotation = parent.transform.rotation * new UnityEngine.Quaternion(-mesh.Prim.Rotation.X, -mesh.Prim.Rotation.Y, mesh.Prim.Rotation.Z, mesh.Prim.Rotation.W);
		obj.transform.localScale = new UnityEngine.Vector3(mesh.Prim.Scale.X, mesh.Prim.Scale.Y, mesh.Prim.Scale.Z);
		objects[mesh.Prim.LocalID] = obj;
		obj.transform.parent = primObjects.transform;
		//Debug.Log("prim " + mesh.Prim.LocalID.ToString() + ": Pos,"+mesh.Prim.Position.ToString() + " Rot,"+mesh.Prim.Rotation.ToString() + " Scale,"+mesh.Prim.Scale.ToString());
		//Sadly, when it comes to non-uniform scale parent, Unity will skew the child, so we cannot make hierachy of the objects.
	}
	
	// Update is called once per frame
	void Update () {
		//Update prims
		lock (newPrims)
		{
			foreach (var item in newPrims)
			{			
				ProcessPrim(item.Value);
			}
			newPrims.Clear();
		}
		
		//Update avatars
		foreach (var item in newAvatars)
		{
			ProcessAvatar(item.Value);
		}
		newAvatars.Clear();
		
		//Update avatar textures
		if (avHasTex.Count > 0)
			UpdateAvTexture(avHasTex[0]);//remove item in the func
		
		//Update avatar movement
		foreach (var item in newTerseAvatarUpdates)
		{
			MoveAvatar(item);
		}
		newTerseAvatarUpdates.Clear();
		
		//Update terrain
		terrainTimeSinceUpdate += Time.deltaTime;
		if (terrainModified && terrainTimeSinceUpdate > MinimumTimeBetweenTerrainUpdated)
		{
			UpdateTerrain();
		}			
		
		if (terrainTextureNeedsUpdate)
        	UpdateTerrainTexture();
		
		//process keyboard to move avatar
		if (Client.Network.Connected)
		{
			UpdateAgentMovement(Time.deltaTime);	
			UpdateCamera();			
		}
		else
			ClearScene();
	}
	
	bool isHoldingHome = false;
	void UpdateAgentMovement(float time)
	{
		Client.Self.Movement.AtPos = Input.GetAxisRaw("Vertical") > 0;
		Client.Self.Movement.AtNeg = Input.GetAxisRaw("Vertical") < 0;
		Client.Self.Movement.TurnLeft = Input.GetAxisRaw("Horizontal") < 0;
		Client.Self.Movement.TurnRight = Input.GetAxisRaw("Horizontal") > 0;
		if (Client.Self.Movement.TurnLeft)
			Client.Self.Movement.BodyRotation *= OpenMetaverse.Quaternion.CreateFromAxisAngle(OpenMetaverse.Vector3.UnitZ, time);
		else if (Client.Self.Movement.TurnRight)
			Client.Self.Movement.BodyRotation *= OpenMetaverse.Quaternion.CreateFromAxisAngle(OpenMetaverse.Vector3.UnitZ, -time);
		Client.Self.Movement.UpPos = Input.GetAxisRaw("Jump") > 0;
		Client.Self.Movement.UpNeg = Input.GetAxisRaw("Jump") < 0;
		
		if (Input.GetAxisRaw("Fly") > 0)
		{
			//Holding the home key only makes it change once, 
            // not flip over and over, so keep track of it
			if (isHoldingHome == false)
			{
				Client.Self.Movement.Fly = !Client.Self.Movement.Fly;
				isHoldingHome = true;
			}
		}
		else
			isHoldingHome = false;	
	}
	
	void UpdateCamera()
	{
		OpenMetaverse.Vector3 camPos = Client.Self.SimPosition +
			new OpenMetaverse.Vector3(-4, 0, 1) * Client.Self.Movement.BodyRotation;
		this.transform.position = new UnityEngine.Vector3(camPos.X, camPos.Y, -camPos.Z);
		
		OpenMetaverse.Vector3 focalPos = Client.Self.SimPosition +
			new OpenMetaverse.Vector3(5, 0, 0) * Client.Self.Movement.BodyRotation;
		this.transform.LookAt(new UnityEngine.Vector3(focalPos.X, focalPos.Y, -focalPos.Z), UnityEngine.Vector3.back);
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
			
			if (face.Vertices[k].TexCoord.X < 0 || face.Vertices[k].TexCoord.X > 1)
				Debug.Log("Texture Repeat!" + face.Vertices[k].TexCoord.X.ToString());
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


        // Login to Simulator
        Client.Network.BeginLogin(loginParams);
		Debug.Log("Start Connecting");
	}
	
	void Appearance_AppearanceSet(object sender, AppearanceSetEventArgs e)
	{
		if (e.Success)
		{
			OpenMetaverse.Avatar me;
			if (Client.Network.CurrentSim.ObjectsAvatars.TryGetValue(Client.Self.LocalID, out me))
			{
				DownloadAVTextures(me);
				if (!avHasTex.Contains(Client.Self.LocalID))
					avHasTex.Add(Client.Self.LocalID);
			}
		}
	}
	
	void Avatars_AvatarAppearance(object sender, AvatarAppearanceEventArgs e)
	{            
		if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;

        OpenMetaverse.Avatar a = e.Simulator.ObjectsAvatars.Find(av => av.ID == e.AvatarID);
        if (a != null)
        {
			DownloadAVTextures(a);
        	avHasTex.Add(a.LocalID);
        }
	}
	
	void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
    {
		if (e.Prim.PrimData.PCode == PCode.Avatar)
		{
			newTerseAvatarUpdates.Add(e);
		}         
	}
	
	void MoveAvatar(TerseObjectUpdateEventArgs e)
	{
		GameObject av = avatars[e.Prim.LocalID];
		UnityEngine.Vector3 pos;
		pos.x = e.Prim.Position.X;
		pos.y = e.Prim.Position.Y;
		pos.z = -(e.Prim.Position.Z - 0.7f); //hack: fix foot to ground
		av.transform.position = pos;
		
		UnityEngine.Quaternion rot;
		rot.x = e.Prim.Rotation.X;
		rot.y = e.Prim.Rotation.Y;
		rot.z = e.Prim.Rotation.Z;
		rot.w = e.Prim.Rotation.W;
		av.transform.rotation = rot;
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
			ClearScene();
		}
	}
	
	void ClearScene()
	{
		//clear objects
		foreach (var item in objects)
			Destroy(item.Value);
		objects.Clear();
		newPrims.Clear();
		
		//clear avatars
		foreach (var item in avatars)
			Destroy(item.Value);
		avatars.Clear();
		renderAvatars.Clear();
		avHasTex.Clear();
		newAvatars.Clear();
		newTerseAvatarUpdates.Clear();
		
		//clear terrain
		mesh1.Clear();
		mesh2.Clear();
	}
	
	void DownloadTexture(UUID textureID)
	{
		if (!textures.ContainsKey(textureID))
		{
			if (Client.Assets.Cache.HasAsset(textureID))
			{
				Debug.Log("Cache hits!");
				byte[] jpg = Client.Assets.Cache.GetCachedAssetBytes(textureID);
				ManagedImage mi;
				if (!OpenJPEG.DecodeToImage(jpg, out mi)) return;
				byte[] imageBytes = mi.ExportTGA();
				Bitmap img;
				using (MemoryStream byteData = new MemoryStream(imageBytes))
				{
					img = LoadTGAClass.LoadTGA(byteData);
				}								
				bitmaps[textureID] = img;
			}
			else
			{
				TextureDownloadCallback handler = (state, asset) =>
				{
					Debug.Log("state is " + state.ToString());
					try{
                	switch (state)
                    {
                    	case TextureRequestState.Finished:
						{
							ManagedImage mi;
							if (!OpenJPEG.DecodeToImage(asset.AssetData, out mi)) break;
							byte[] imageBytes = mi.ExportTGA();
							Bitmap img;
							using (MemoryStream byteData = new MemoryStream(imageBytes))
							{
								img = LoadTGAClass.LoadTGA(byteData);
							}								
							bitmaps[textureID] = img;
                    		break;
						}                              	
   						case TextureRequestState.Aborted:
                    	case TextureRequestState.NotFound:
                    	case TextureRequestState.Timeout:
                    		break;
                    }
					}
					catch(Exception ex)
					{
						Debug.Log("what happened?:" + ex.Message);
					}
                };
				
				Client.Assets.RequestImage(textureID, ImageType.Normal, handler);
			}
		}
	}
	
    void Objects_OnObjectUpdate(object sender, PrimEventArgs e)
    {
		//leave tree out temporarily. Radegast doesn't implement tree rendering yet.
		if (e.Prim.PrimData.PCode != PCode.Prim)
		{
			Debug.Log("Receive " + e.Prim.PrimData.PCode.ToString());
			return;
		}
		FacetedMesh mesh = null;
		
		//FIXME : need to update prims?
		if (objects.ContainsKey(e.Prim.LocalID))
		{
			Debug.Log ("receive prim with LocalID " + e.Prim.LocalID.ToString() + " again!");
			return;
		}
				
		if (e.Prim.Sculpt != null)
		{
			//leave sculpt prim out temporarily
		}
		else
		{
			mesh = R.GenerateFacetedMesh(e.Prim, DetailLevel.Highest);
			lock (newPrims)
			{
				newPrims[e.Prim.LocalID] = mesh;
			}
			foreach (Face face in mesh.Faces)
			{
				DownloadTexture(face.TextureFace.TextureID);
			}             
		}
	}
	
	void Objects_AvatarUpdate(object sender, AvatarUpdateEventArgs e)
	{
		if (e.Avatar.PrimData.PCode == PCode.Avatar)
			Debug.Log ("Recieve an Avatar!!!");
		Radegast.Rendering.GLAvatar ga = new Radegast.Rendering.GLAvatar();
		OpenMetaverse.Avatar av = e.Avatar;
                    
        Radegast.Rendering.RenderAvatar ra = new Radegast.Rendering.RenderAvatar();
        ra.avatar = av;
        ra.glavatar = ga;

        newAvatars.Add(av.LocalID, ra);
        ra.glavatar.morph(av);
	}
	
	void DownloadAVTextures(OpenMetaverse.Avatar a)
	{
		foreach (Primitive.TextureEntryFace TEF in a.Textures.FaceTextures)
		{			
			if (TEF == null) continue;
			DownloadTexture(TEF.TextureID);
		}
	}
	
	void ProcessAvatar(Radegast.Rendering.RenderAvatar av)
	{
		GameObject avatarGameObject = new GameObject(av.avatar.LocalID.ToString());
		avatarGameObject.transform.position = new UnityEngine.Vector3(av.avatar.Position.X, av.avatar.Position.Y, -av.avatar.Position.Z);
		foreach (Radegast.Rendering.GLMesh mesh in av.glavatar._meshes.Values)
		{
			if (av.glavatar._showSkirt == false && mesh.Name == "skirtMesh") continue;
			
			UnityEngine.Vector3[] vertices = new UnityEngine.Vector3[mesh.RenderData.Vertices.Length / 3];
			UnityEngine.Vector2[] uvs = new UnityEngine.Vector2[mesh.RenderData.Vertices.Length / 3];
			UnityEngine.Vector3[] normals = new UnityEngine.Vector3[mesh.RenderData.Vertices.Length / 3];
			int[] triangles = new int[mesh.RenderData.Indices.Length];
			for (int i = 0; i < mesh.RenderData.Vertices.Length / 3; ++i)
			{
				vertices[i].x = mesh.RenderData.Vertices[3*i];
				vertices[i].y = mesh.RenderData.Vertices[3*i+1];
				vertices[i].z = -mesh.RenderData.Vertices[3*i+2];
				
				uvs[i].x = mesh.RenderData.TexCoords[2*i];
				uvs[i].y = mesh.RenderData.TexCoords[2*i+1];
				
				normals[i].x = mesh.RenderData.Normals[3*i];
				normals[i].y = mesh.RenderData.Normals[3*i+1];
				normals[i].z = -mesh.RenderData.Normals[3*i+2];
			}
			
			for (int i = 0; i < mesh.RenderData.Indices.Length; i += 3)
			{
				//HACK: OpenGL's default front face is counter-clock-wise
				triangles[i] = mesh.RenderData.Indices[i+2];
				triangles[i+1] = mesh.RenderData.Indices[i+1];
				triangles[i+2] = mesh.RenderData.Indices[i];
			}
			
			GameObject part = new GameObject(mesh.Name);
			part.AddComponent("MeshFilter");
			part.transform.parent = avatarGameObject.transform;
			part.transform.localPosition = new UnityEngine.Vector3(0, 0, 0);
			UnityEngine.Mesh meshUnity = (part.GetComponent<MeshFilter>() as MeshFilter).mesh;
							
			meshUnity.vertices = vertices;
			meshUnity.uv = uvs;
			meshUnity.triangles = triangles;
			meshUnity.normals = normals;
			
			UnityEngine.Material mat = (part.AddComponent("MeshRenderer") as MeshRenderer).material;
			mat = m;
		}
		avatars.Add(av.avatar.LocalID, avatarGameObject);
		renderAvatars.Add(av.avatar.LocalID, av);
		avatarGameObject.transform.parent = avatarObjects.transform;
	}
	
	void UpdateAvTexture(uint avLocalID)
	{
		bool del = true;
		GameObject avatarGameObject = avatars[avLocalID];
		Radegast.Rendering.RenderAvatar ra = renderAvatars[avLocalID];
		foreach (Radegast.Rendering.GLMesh mesh in ra.glavatar._meshes.Values)
		{
			if (mesh.Name == "skirtMesh") continue;
			UUID texID = ra.avatar.Textures.GetFace((uint)mesh.teFaceID).TextureID;
			Transform child = avatarGameObject.transform.FindChild(mesh.Name);
			UnityEngine.Material mat = child.GetComponent<MeshRenderer>().material;
			if (textures.ContainsKey(texID))
				mat.mainTexture = textures[texID];
			else if (bitmaps.ContainsKey(texID))
			{
				Texture2D tex = Bitmap2Texture2D(bitmaps[texID]);
				textures[texID] = tex;
				mat.mainTexture = tex;
				bitmaps.Remove(texID);
			}
			else
				del = false;
		}
		if (del)
			avHasTex.Remove(avLocalID);
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
