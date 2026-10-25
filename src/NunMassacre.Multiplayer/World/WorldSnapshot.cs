using System;
using UnityEngine;

namespace NunMassacre.Multiplayer.World
{
    // JsonUtility uses fields and arrays. No runtime type names are instantiated from network data.
    [Serializable] internal sealed class WorldSnapshot
    {
        public int version = 1;
        public string scene;
        public bool full;
        public int registryCount;
        public string registryDigest;
        public ObjectSnapshot[] objects;
    }

    [Serializable] internal sealed class ObjectSnapshot
    {
        public string id;
        public bool destroyed;
        public bool active;
        public bool pose;
        public string parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public ComponentSnapshot[] components;
    }

    [Serializable] internal sealed class ComponentSnapshot
    {
        public string id;
        public string kind;
        public bool enabled;
        public bool flag;
        public int integer;
        public float number;
        public float number2;
        public float number3;
        public string text;
        public Color color;
        public ValueSnapshot[] values;
        public AnimatorLayerSnapshot[] layers;
        public AnimatorParameterSnapshot[] parameters;
    }

    [Serializable] internal sealed class ValueSnapshot
    {
        public string name;
        public string value;
    }

    [Serializable] internal sealed class AnimatorLayerSnapshot
    {
        public int state;
        public float time;
        public float weight;
    }

    [Serializable] internal sealed class AnimatorParameterSnapshot
    {
        public int hash;
        public int type;
        public float number;
        public int integer;
        public bool flag;
    }
}
