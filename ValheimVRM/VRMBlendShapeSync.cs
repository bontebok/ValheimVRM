using System;
using System.Collections.Generic;
using UnityEngine;
using VRM;

namespace ValheimVRM
{
    public class VRMBlendShapeSync : MonoBehaviour
    {
        private VRMBlendShapeProxy proxy;
        private GameObject vrm;
        private SkinnedMeshRenderer[] allSmr;

        public class BlendshapeMap
        {
            public SkinnedMeshRenderer Smr;
            public int Index;
        }

        private Dictionary<string, BlendshapeMap> blendshapeDict = new Dictionary<string, BlendshapeMap>
        {
            { "v_armorswap_rag", null },
            { "v_armorswap_leather", null },
            { "v_armorswap_troll", null },
            { "v_armorswap_root", null },
            { "v_armorswap_bronze", null },
            { "v_armorswap_iron", null },
            { "v_armorswap_fenris", null },
            { "v_armorswap_wolf", null },
            { "v_armorswap_padded", null },
            { "v_armorswap_etirweave", null },
            { "v_armorswap_carapace", null }
        };

        string GetRelativePath(SkinnedMeshRenderer smr)
        {
            Transform current = smr.transform;
            string path = current.name;
            while (current.parent != null && current.parent != vrm.transform)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }
        void AddBlendShapeClip(SkinnedMeshRenderer smr, int i, string blendShapeName)
        {
            var avatar = proxy.BlendShapeAvatar;
            
            bool exists = avatar.Clips.Exists(existingClip => existingClip.BlendShapeName == blendShapeName);
            if (exists)
            {
                Debug.LogWarning($"BlendShapeClip '{blendShapeName}' already exists. Skipping.");
                return;
            }
            Debug.Log($"BlendShapeClip Adding: {blendShapeName}");


            var clip = ScriptableObject.CreateInstance<BlendShapeClip>();
 
            // unity asset name
            clip.name = blendShapeName;
            // vrm export name
            clip.BlendShapeName = blendShapeName;
            clip.Preset = BlendShapePreset.Unknown;

            clip.IsBinary = false;
            clip.Values = new BlendShapeBinding[]
            {
                new BlendShapeBinding
                {
                    RelativePath = GetRelativePath(smr), // target Renderer relative path from root 
                    Index = i, 
                    Weight = 100f 
                },
            };
            clip.MaterialValues = Array.Empty<MaterialValueBinding>();
            // clip.MaterialValues = new MaterialValueBinding[]
            // {
            //     new MaterialValueBinding
            //     {
            //         MaterialName = "Target", // target material name
            //         ValueName = "_Color", // target material property name,
            //         BaseValue = new Vector4(1, 1, 1, 1), // Target value when the Weight value of BlendShapeClip is 0
            //         TargetValue = new Vector4(0, 0, 0, 1), // Target value when the Weight value of BlendShapeClip is 1
            //     },
            // };
            avatar.Clips.Add(clip);

        }
        
        public void Setup(GameObject vrm)
        {
            this.proxy = vrm.GetComponent<VRMBlendShapeProxy>();
            this.vrm = vrm;

            this.allSmr = vrm.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in allSmr)
            {
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string blendshapeName = smr.sharedMesh.GetBlendShapeName(i);

                    if (blendshapeDict.ContainsKey(blendshapeName))
                    {
                        Debug.Log("Armor Blendshape Found: " + blendshapeName);
                        AddBlendShapeClip(smr, i, blendshapeName);
                        blendshapeDict[blendshapeName] = new BlendshapeMap()
                        {
                            Smr = smr,
                            Index = i
                        };
                    }
                }
            }
        }

        public BlendshapeMap this[string blendshapeName]
        {
            get
            {
                if (blendshapeDict.TryGetValue(blendshapeName, out var blendshape))
                {
                    return blendshape;
                }
                throw new KeyNotFoundException($"Blendshape {blendshapeName} not found!");
            }
        }

        void Update()
        {
            var values = proxy.GetValues();

            foreach (var clip in proxy.BlendShapeAvatar.Clips)
            {
                foreach (var binding in clip.Values)
                {
                    var meshBlendshapeName = binding.RelativePath;
                    var weight = binding.Weight;

                    if (blendshapeDict.TryGetValue(meshBlendshapeName, out var blendshape))
                    {
                        blendshape.Smr.SetBlendShapeWeight(blendshape.Index, weight);
                    }
                }
            }
        }
    }
}
