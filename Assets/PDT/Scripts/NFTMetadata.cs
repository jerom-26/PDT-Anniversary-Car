using System;
using UnityEngine;

[Serializable]public class NFTMetadata
{
    public string name;
    public string description; 
    public string image;
    public NFTAttribute[] attributes;
}

[Serializable]
public class NFTAttribute
{
    public string trait_type;
    public string value;
}