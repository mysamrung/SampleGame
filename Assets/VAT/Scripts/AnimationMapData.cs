using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class AnimationMapData : ScriptableObject
{
    [System.Serializable]
    public class AnimationMapInfo
    {
        public string animationName = "";
        public List<string> meshNames = new();
        public List<Texture> vatList = new();
        public float peakFrame = 0f;                // 観客のアニメーションでBPMの拍に合わせるフレームを指定
    }

    [SerializeField] private List<AnimationMapInfo> animationMapList = new ();

    public List<AnimationMapInfo> List => animationMapList;
}
