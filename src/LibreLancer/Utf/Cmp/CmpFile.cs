﻿// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package


using System;
using System.Collections.Generic;
using System.Linq;

using LibreLancer.Utf.Vms;
using LibreLancer.Utf.Anm;
using LibreLancer.Utf.Mat;
using LibreLancer.Physics.Sur;

namespace LibreLancer.Utf.Cmp
{
    /// <summary>
    /// Represents a UTF Compound File (.cmp)
    /// </summary>
    public class CmpFile : UtfFile, IRigidModelFile, ILibFile
    {
        private ILibFile additionalLibrary;

        public string Path { get; set; }

        public VmsFile VMeshLibrary { get; private set; }
        public AnmFile Animation { get; private set; }
        public MatFile MaterialLibrary { get; private set; }
        public TxmFile TextureLibrary { get; private set; }
		public MaterialAnimCollection MaterialAnim { get; private set; }

		public List<Part> Parts { get; private set; }
        public ConstructCollection Constructs { get; private set; }
        public Dictionary<string, ModelFile> Models { get; private set; }
        public Dictionary<string, CmpCameraInfo> Cameras { get; private set; }

        public CmpFile(string path, ILibFile additionalLibrary) : this(parseFile(path), additionalLibrary)
		{
			Path = path;
		}

        public IEnumerable<Part> ModelParts() => Parts.Where(x => x.Camera == null);

        public Part GetRootPart()
        {
            foreach (var part in Parts)
            {
                if (part.ObjectName.Equals("Root", StringComparison.OrdinalIgnoreCase)) return part;
            }
            return null;
        }

        public CmpFile(IntermediateNode rootnode, ILibFile additionalLibrary)
        {
            this.additionalLibrary = additionalLibrary;

            Models = new Dictionary<string, ModelFile>();
            Cameras = new Dictionary<string, CmpCameraInfo>();
            Constructs = new ConstructCollection();
            Parts = new List<Part>();
            List<string> modelNames = new List<string>(); 
			foreach (Node node in rootnode)
            {
                switch (node.Name.ToLowerInvariant())
                {
                    case "exporter version":
                        break;
                    case "vmeshlibrary":
                        IntermediateNode vMeshLibraryNode = node as IntermediateNode;
                        if (VMeshLibrary == null) VMeshLibrary = new VmsFile(vMeshLibraryNode, this);
                        else throw new Exception("Multiple vmeshlibrary nodes in cmp root");
                        break;
                    case "animation":
                        IntermediateNode animationNode = node as IntermediateNode;
                        if (Animation == null) Animation = new AnmFile(animationNode, Constructs);
                        else throw new Exception("Multiple animation nodes in cmp root");
                        break;
                    case "material library":
                        IntermediateNode materialLibraryNode = node as IntermediateNode;
                        if (MaterialLibrary == null) MaterialLibrary = new MatFile(materialLibraryNode, this);
                        else throw new Exception("Multiple material library nodes in cmp root");
                        break;
                    case "texture library":
                        IntermediateNode textureLibraryNode = node as IntermediateNode;
                        if (TextureLibrary == null) TextureLibrary = new TxmFile(textureLibraryNode);
                        else throw new Exception("Multiple texture library nodes in cmp root");
                        break;
                    case "cmpnd":
                        IntermediateNode cmpndNode = node as IntermediateNode;
                        foreach (Node SubNode in cmpndNode)
                        {
							if (SubNode is LeafNode) continue;
							var cmpndSubNode = (IntermediateNode)SubNode;
                            if (cmpndSubNode.Name.Equals("cons", StringComparison.OrdinalIgnoreCase))
                            {
                                Constructs.AddNode(cmpndSubNode);
                            }
                            else if (
                                cmpndSubNode.Name.StartsWith("part_", StringComparison.OrdinalIgnoreCase) ||
                                cmpndSubNode.Name.Equals("root", StringComparison.OrdinalIgnoreCase)
                            )
                            {
                                string objectName = string.Empty, fileName = string.Empty;

                                foreach (LeafNode partNode in cmpndSubNode)
                                {
                                    switch (partNode.Name.ToLowerInvariant())
                                    {
                                        case "object name":
                                            objectName = partNode.StringData;
                                            break;
                                        case "file name":
                                            fileName = partNode.StringData;
                                            break;
										case "index":
                                            break;
                                        default: 
                                            FLLog.Error("Cmp","Invalid node in " + cmpndSubNode.Name + ": " + partNode.Name);
                                            break;
                                    }
                                }
								Parts.Add(new Part(objectName, fileName, Models, Cameras, Constructs));
                            }
                            else throw new Exception("Invalid node in " + cmpndNode.Name + ": " + cmpndSubNode.Name);
                        }
                        break;
                    case "materialanim":
						MaterialAnim = new MaterialAnimCollection((IntermediateNode)node);
                        break;
                    default:
                        if(node is IntermediateNode)
                        {
                            var im = (IntermediateNode)node;
                            if(im.Any(x => x.Name.Equals("vmeshpart",StringComparison.OrdinalIgnoreCase) ||
                                x.Name.Equals("multilevel",StringComparison.OrdinalIgnoreCase)))
                            {
                                ModelFile m = new ModelFile(im, this);
                                m.Path = node.Name;
                                Models.Add(node.Name, m);
                                modelNames.Add(node.Name);
                                break;
                            }
                            else if (im.Any(x => x.Name.Equals("camera",StringComparison.OrdinalIgnoreCase)))
                            {
                                var cam = new CmpCameraInfo(im);
                                Cameras.Add(im.Name, cam);
                                break;
                            }
                        }
                        FLLog.Error("Cmp", Path ?? "Utf" + ": Invalid Node in cmp root: " + node.Name);
                        break;
                }
            }
            //FL handles cmpnd nodes that point to non-existant models: fix up here
            List<Part> broken = new List<Part>();
            for (int i = 0; i < Parts.Count; i++) {
                if (Parts[i].IsBroken()) broken.Add(Parts[i]);
            }
            foreach (var b in broken) Parts.Remove(b);
        }

		public void Initialize(ResourceManager cache)
        {
            foreach (var part in Parts) part.Initialize(cache);
        }
        
        public Texture FindTexture(string name)
        {
           	return additionalLibrary.FindTexture(name);
        }

        public Material FindMaterial(uint materialId)
        {
           	return additionalLibrary.FindMaterial(materialId);
        }

        
        public VMeshData FindMesh(uint vMeshLibId)
        {
            if (VMeshLibrary != null)
            {
                VMeshData mesh = VMeshLibrary.FindMesh(vMeshLibId);
                if (mesh != null) return mesh;
            }
            if (additionalLibrary != null) return additionalLibrary.FindMesh(vMeshLibId);
            return null;
        }

        public RigidModel CreateRigidModel(bool drawable)
        {
            var mdl = new RigidModel() {Path = Path};
            mdl.Parts = new Dictionary<string, RigidModelPart>(StringComparer.OrdinalIgnoreCase);
            var rp = GetRootPart();
            List<RigidModelPart> allParts = new List<RigidModelPart>();
            foreach (var p in Parts)
            {
                var mdlPart = p.Model.CreatePart(drawable);
                mdlPart.Name = p.ObjectName;
                mdlPart.Path = p.FileName;
                if (p.Construct != null)
                {
                    mdlPart.Construct = p.Construct.Clone();
                }
                mdlPart.Children = new List<RigidModelPart>();
                allParts.Add(mdlPart);
            }
            foreach (var p in allParts)
            {
                mdl.Parts.Add(p.Name, p);
                if (p.Construct != null)
                {
                    var parent = allParts.First(x =>
                        x.Name.Equals(p.Construct.ParentName, StringComparison.OrdinalIgnoreCase));
                    parent.Children.Add(p);
                }
                else
                    mdl.Root = p;
            }
            mdl.AllParts = allParts.ToArray();
            mdl.MaterialAnims = MaterialAnim;
            mdl.Animation = Animation;
            mdl.UpdateTransform();
            return mdl;
        }
        
        public SurPart ToSurHierarchy(out Dictionary<Part, SurPart> surParts)
        {
            surParts = new Dictionary<Part, SurPart>();
            foreach (var part in Parts)  {
                var sp = new SurPart() {Children = new List<SurPart>(), Hash = CrcTool.FLModelCrc(part.ObjectName)};
                surParts.Add(part, sp);
            }
            foreach (var part in Parts)
            {
                if (part.Construct != null)
                {
                    var p = Parts.FirstOrDefault((x) =>
                        x.ObjectName.Equals(part.Construct.ParentName, StringComparison.OrdinalIgnoreCase));
                    if (p != null) surParts[p].Children.Add(surParts[part]);
                }
            }
            return surParts[GetRootPart()];
        }

        public override string ToString()
        {
            return Path;
        }
    }
}