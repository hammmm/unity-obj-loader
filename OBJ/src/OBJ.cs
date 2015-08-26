using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;

public class OBJ : MonoBehaviour {
	
	public string objPath;
#if UNITY_5
	public bool useLegacyShaders = false; // compatibility option for previous users of OBJ.cs using Unity5
#else
	private bool useLegacyShaders = true;
#endif

	/* OBJ file tags */
	private const string O 	= "o";
	private const string G 	= "g";
	private const string V 	= "v";
	private const string VT = "vt";
	private const string VN = "vn";
	private const string F 	= "f";
	private const string MTL = "mtllib";
	private const string UML = "usemtl";

	/* MTL file tags */
	private const string NML = "newmtl";
	private const string NS = "Ns"; // Shininess
	private const string KA = "Ka"; // Ambient component (not supported)
	private const string KD = "Kd"; // Diffuse component
	private const string KS = "Ks"; // Specular component
	private const string D = "d"; 	// Transparency (not supported)
	private const string TR = "Tr";	// Same as 'd'
	private const string ILLUM = "illum"; // Illumination model. 1 - diffuse, 2 - specular
	private const string MAP_KA = "map_Ka"; // Ambient texture
	private const string MAP_KD = "map_Kd"; // Diffuse texture
	private const string MAP_KS = "map_Ks"; // Specular texture
	private const string MAP_KE = "map_Ke"; // Emissive texture
	private const string MAP_BUMP = "map_bump"; // Bump map texture
	private const string BUMP = "bump"; // Bump map texture

	/* Material shaders */
#if UNITY_5
	private const string DIFFUSE_SHADER = "Standard";
	private const string SPECULAR_SHADER = "Standard (Specular setup)";
	private const string BUMPED_DIFFUSE_SHADER = "Standard";
	private const string BUMPED_SPECULAR_SHADER = "Standard (Specular setup)";
	
	private const string LEGACY_DIFFUSE_SHADER = "Legacy Shaders/Diffuse";
	private const string LEGACY_SPECULAR_SHADER = "Legacy Shaders/Specular";
	private const string LEGACY_BUMPED_DIFFUSE_SHADER = "Legacy Shaders/Bumped Diffuse";
	private const string LEGACY_BUMPED_SPECULAR_SHADER = "Legacy Shaders/Bumped Specular";
#else
	private const string DIFFUSE_SHADER = "Diffuse";
	private const string SPECULAR_SHADER = "Specular";
	private const string BUMPED_DIFFUSE_SHADER = "Bumped Diffuse";
	private const string BUMPED_SPECULAR_SHADER = "Bumped Specular";

	private const string LEGACY_DIFFUSE_SHADER = DIFFUSE_SHADER;
	private const string LEGACY_SPECULAR_SHADER = SPECULAR_SHADER;
	private const string LEGACY_BUMPED_DIFFUSE_SHADER = BUMPED_DIFFUSE_SHADER;
	private const string LEGACY_BUMPED_SPECULAR_SHADER = BUMPED_SPECULAR_SHADER;
#endif


	private string basepath;
	private string mtllib;
	private GeometryBuffer buffer;

	void Start ()
	{
		buffer = new GeometryBuffer ();

		StartCoroutine (Load (objPath));
	}
	
	public IEnumerator Load(string path) {
		yield return 0; // play nice by not hogging the main thread

		basepath = (path.IndexOf("/") == -1) ? "" : path.Substring(0, path.LastIndexOf("/") + 1);

		WWW loader = new WWW(path);
		yield return loader;
		yield return StartCoroutine(SetGeometryData(loader.text));
		
		if(hasMaterials) {
			Debug.Log("base path = "+basepath);
			Debug.Log("MTL path = "+(basepath + mtllib));
			string mtlPath = basepath + mtllib;
			loader = new WWW (mtlPath);
			yield return loader;

			if (loader.error != null) {
				Debug.LogError(loader.error);
			}
			else {
				SetMaterialData(loader.text);
			}

			if (materialData != null) {
				foreach(MaterialData m in materialData) {
					if(m.diffuseTexPath != null) {
						string texpath = basepath + m.diffuseTexPath;
						loader = new WWW(texpath);
						yield return loader;

						if (loader.error != null) {
							Debug.LogError(loader.error);
						} else {
							m.diffuseTex = GetTexture(loader);
						}
					}

					if(m.bumpTexPath != null) {
						string texpath = basepath + m.bumpTexPath;
						loader = new WWW(texpath);
						yield return loader;

						if (loader.error != null) {
							Debug.LogError(loader.error);
						} else {
							m.bumpTex = GetTexture(loader);
						}
					}
				}
			}
		}

		Build();
	}

	private Texture2D GetTexture(WWW loader) {
		string ext = Path.GetExtension(loader.url).ToLower();
		if (ext != ".png" && ext != ".jpg" && ext != ".tga") {
			Debug.LogWarning("maybe unsupported texture format:"+ext);
		}

		return (ext == ".tga" ? TGALoader.LoadTGA (new MemoryStream(loader.bytes)) : loader.texture);
		// refactor this method to add support for more formats
	}
	
	private void GetFaceIndicesByOneFaceLine(FaceIndices[] faces, string[] p, bool isFaceIndexPlus) {
		if (isFaceIndexPlus) {
			for(int j = 1; j < p.Length; j++) {
				string[] c = p[j].Trim().Split("/".ToCharArray());
				FaceIndices fi = new FaceIndices();
				// vertex
				int vi = ci(c[0]);
				fi.vi = vi-1;
				// uv
				if(c.Length > 1 && c[1] != "") {
					int vu = ci(c[1]);
					fi.vu = vu-1;
				}
				// normal
				if(c.Length > 2 && c[2] != "") {
					int vn = ci(c[2]);
					fi.vn = vn-1;
				}
				else { 
					fi.vn = -1;
				}
				faces[j-1] = fi;
			}
		}
		else { // for minus index
			int vertexCount = buffer.vertices.Count;
			int uvCount = buffer.uvs.Count;
			for(int j = 1; j < p.Length; j++) {
				string[] c = p[j].Trim().Split("/".ToCharArray());
				FaceIndices fi = new FaceIndices();
				// vertex
				int vi = ci(c[0]);
				fi.vi = vertexCount + vi;
				// uv
				if(c.Length > 1 && c[1] != "") {
					int vu = ci(c[1]);
					fi.vu = uvCount + vu;
				}
				// normal
				if(c.Length > 2 && c[2] != "") {
					int vn = ci(c[2]);
					fi.vn = vertexCount + vn;
				}
				else {
					fi.vn = -1;
				}
				faces[j-1] = fi;
			}
		}
	}

	private IEnumerator SetGeometryData(string data) {
		yield return 0; // play nice by not hogging the main thread

		string[] lines = data.Split("\n".ToCharArray());

		bool isFirstInGroup = true;
		bool isFaceIndexPlus = true;

		for(int i = 0; i < lines.Length; i++) {
			string l = lines[i].Trim();

			if(l.Length > 0 && l[0] == '#') { // comment line
				continue;
			}
			string[] p = l.Replace("  ", " ").Split(' ');

			switch(p[0]) {
				case O:
					buffer.PushObject(p[1].Trim());
					isFirstInGroup = true;
					break;
				case G:
					string groupName = null;
					if (p.Length >= 2) {
						groupName = p[1].Trim();
					}
					isFirstInGroup = true;
					buffer.PushGroup(groupName);
					break;
				case V:
					buffer.PushVertex( new Vector3( cf(p[1]), cf(p[2]), cf(p[3]) ) );
					break;
				case VT:
					buffer.PushUV(new Vector2( cf(p[1]), cf(p[2]) ));
					break;
				case VN:
					buffer.PushNormal(new Vector3( cf(p[1]), cf(p[2]), cf(p[3]) ));
					break;
				case F:
					FaceIndices[] faces = new FaceIndices[p.Length-1];
					if (isFirstInGroup) {
						isFirstInGroup = false;
						string[] c = p[1].Trim().Split("/".ToCharArray());
						isFaceIndexPlus = (ci(c[0]) >= 0);
					}
					GetFaceIndicesByOneFaceLine(faces, p, isFaceIndexPlus);
					if (p.Length == 4) {
						buffer.PushFace(faces[0]);
						buffer.PushFace(faces[1]);
						buffer.PushFace(faces[2]);
					}
					else if (p.Length == 5) {
						buffer.PushFace(faces[0]);
						buffer.PushFace(faces[1]);
						buffer.PushFace(faces[3]);
						buffer.PushFace(faces[3]);
						buffer.PushFace(faces[1]);
						buffer.PushFace(faces[2]);
					}
					else {
						Debug.LogWarning("face vertex count :"+(p.Length-1)+" larger than 4:");
					}
					break;
				case MTL:
					mtllib = l.Substring(p[0].Length+1).Trim();
					break;
				case UML:
					buffer.PushMaterialName(p[1].Trim());
					break;
			}
			if (i % 7000 == 0) yield return 0; // play nice with main thread while parsing large objs
		}
		// buffer.Trace();
	}

	private float cf(string v) {
		try {
			return float.Parse(v);
		}
		catch(Exception e) {
			Debug.LogError("Error parsing: " + v + ": " + e);
			return 0;
		}
	}
	
	private int ci(string v) {
		try {
			return int.Parse(v);
		}
		catch(Exception e) {
			Debug.LogError(e);
			return 0;
		}
	}
	
	private bool hasMaterials {
		get {
			return mtllib != null;
		}
	}
	
	/* ############## MATERIALS */
	private List<MaterialData> materialData;
	private class MaterialData {
		public string name;
		public Color ambient;
		public Color diffuse;
		public Color specular;
		public float shininess;
		public float alpha;
		public int illumType;
		public string diffuseTexPath;
		public string bumpTexPath;
		public Texture2D diffuseTex;
		public Texture2D bumpTex;
	}
	
	private void SetMaterialData(string data) {
		string[] lines = data.Split("\n".ToCharArray());
		
		materialData = new List<MaterialData>();
		MaterialData current = new MaterialData();
		Regex regexWhitespaces = new Regex(@"\s+");

		for(int i = 0; i < lines.Length; i++) {
			string l = lines[i].Trim();
			
			if(l.IndexOf("#") != -1) l = l.Substring(0, l.IndexOf("#"));
			string[] p = regexWhitespaces.Split(l);
			if (p[0].Trim() == "") continue;

			switch(p[0]) {
				case NML:
					current = new MaterialData();
					current.name = p[1].Trim();
					materialData.Add(current);
					break;
				case KA:
					current.ambient = gc(p);
					break;
				case KD:
					current.diffuse = gc(p);
					break;
				case KS:
					current.specular = gc(p);
					break;
				case NS:
					current.shininess = cf(p[1]) / 1000;
					break;
				case D:
				case TR:
					current.alpha = cf(p[1]);
					break;
				case MAP_KD:
					current.diffuseTexPath = p[p.Length-1].Trim();
					break;
				case MAP_BUMP:
				case BUMP:
					BumpParameter(current, p);
					break;
				case ILLUM:
					current.illumType = ci(p[1]);
					break;
				default:
					Debug.Log("this line was not processed :" +l );
					break;
			}
		}
	}
	
	private Material GetMaterial(MaterialData md) {
		Material m;
		string shaderName;
		
		if(md.illumType == 2) {
			if (useLegacyShaders) {
				shaderName = (md.bumpTex != null)? LEGACY_BUMPED_SPECULAR_SHADER : LEGACY_SPECULAR_SHADER;
			} else {
				shaderName = (md.bumpTex != null)? BUMPED_SPECULAR_SHADER : SPECULAR_SHADER;
			}
			m =  new Material(Shader.Find(shaderName));
			m.SetColor("_SpecColor", md.specular);
			m.SetFloat("_Shininess", md.shininess);
		} else {
			if (useLegacyShaders) {
				shaderName = (md.bumpTex != null)? LEGACY_BUMPED_DIFFUSE_SHADER : LEGACY_DIFFUSE_SHADER;
			} else {
				shaderName = (md.bumpTex != null)? BUMPED_DIFFUSE_SHADER : DIFFUSE_SHADER;
			}
			m =  new Material(Shader.Find(shaderName));
		}

		if(md.diffuseTex != null) {
			m.SetTexture("_MainTex", md.diffuseTex);
		}
		else {
			m.SetColor("_Color", md.diffuse);
		}
		if(md.bumpTex != null) m.SetTexture("_BumpMap", md.bumpTex);
		
		m.name = md.name;
		
		return m;
	}
	
	private class BumpParamDef {
		public string optionName;
		public string valueType;
		public int valueNumMin;
		public int valueNumMax;
		public BumpParamDef(string name, string type, int numMin, int numMax) {
			this.optionName = name;
			this.valueType = type;
			this.valueNumMin = numMin;
			this.valueNumMax = numMax;
		}
	}

	private void BumpParameter(MaterialData m, string[] p) {
		Regex regexNumber = new Regex(@"^[-+]?[0-9]*\.?[0-9]+$");
		
		var bumpParams = new Dictionary<String, BumpParamDef>();
		bumpParams.Add("bm",new BumpParamDef("bm","string", 1, 1));
		bumpParams.Add("clamp",new BumpParamDef("clamp", "string", 1,1));
		bumpParams.Add("blendu",new BumpParamDef("blendu", "string", 1,1));
		bumpParams.Add("blendv",new BumpParamDef("blendv", "string", 1,1));
		bumpParams.Add("imfchan",new BumpParamDef("imfchan", "string", 1,1));
		bumpParams.Add("mm",new BumpParamDef("mm", "string", 1,1));
		bumpParams.Add("o",new BumpParamDef("o", "number", 1,3));
		bumpParams.Add("s",new BumpParamDef("s", "number", 1,3));
		bumpParams.Add("t",new BumpParamDef("t", "number", 1,3));
		bumpParams.Add("texres",new BumpParamDef("texres", "string", 1,1));
		int pos = 1;
		string filename = null;
		while (pos < p.Length) {
			if (!p[pos].StartsWith("-")) {
				filename = p[pos];
				pos++;
				continue;
			}
			// option processing
			string optionName = p[pos].Substring(1);
			pos++;
			if (!bumpParams.ContainsKey(optionName)) {
				continue;
			}
			BumpParamDef def = bumpParams[optionName];
			ArrayList args = new ArrayList();
			int i=0;
			bool isOptionNotEnough = false;
			for (;i<def.valueNumMin ; i++, pos++) {
				if (pos >= p.Length) {
					isOptionNotEnough = true;
					break;
				}
				if (def.valueType == "number") {
					Match match = regexNumber.Match(p[pos]);
					if (!match.Success) {
						isOptionNotEnough = true;
						break;
					}
				}
				args.Add(p[pos]);
			}
			if (isOptionNotEnough) {
				Debug.Log("bump variable value not enough for option:"+optionName+" of material:"+m.name);
				continue;
			}
			for (;i<def.valueNumMax && pos < p.Length ; i++, pos++) {
				if (def.valueType == "number") {
					Match match = regexNumber.Match(p[pos]);
					if (!match.Success) {
						break;
					}
				}
				args.Add(p[pos]);
			}
			// TODO: some processing of options
			Debug.Log("found option: "+optionName+" of material: "+m.name+" args: "+String.Concat(args.ToArray()));
		}
		if (filename != null) {
			m.bumpTexPath = filename;
		}
	}
	
	private Color gc(string[] p) {
		return new Color( cf(p[1]), cf(p[2]), cf(p[3]) );
	}

	private void Build() {
		Dictionary<string, Material> materials = new Dictionary<string, Material>();
		
		if(hasMaterials) {
			foreach(MaterialData md in materialData) {
				if (materials.ContainsKey(md.name)) {
					Debug.LogWarning("duplicate material found: "+ md.name+ ". ignored repeated occurences");
					continue;
				}
				materials.Add(md.name, GetMaterial(md));
			}
		} else {
			materials.Add("default", new Material(Shader.Find("VertexLit")));
		}
		
		GameObject[] ms = new GameObject[buffer.numObjects];
		
		if(buffer.numObjects == 1) {
			gameObject.AddComponent(typeof(MeshFilter));
			gameObject.AddComponent(typeof(MeshRenderer));
			ms[0] = gameObject;
		} else if(buffer.numObjects > 1) {
			for(int i = 0; i < buffer.numObjects; i++) {
				GameObject go = new GameObject();
				go.transform.parent = gameObject.transform;
				go.AddComponent(typeof(MeshFilter));
				go.AddComponent(typeof(MeshRenderer));
				ms[i] = go;
			}
		}
		
		buffer.PopulateMeshes(ms, materials);
	}
}
