using System;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // Only this fixed message schema crosses the wire. Never deserialize runtime types or invoke received method names.
    [Serializable] public sealed class Packet
    {
        internal const int Protocol = 19;
        public string type;
        public int protocol = Protocol;
        public string build;
        public string text;
        public string settings; // The host's PLAY choices: scenario, Nun skin, item randomisation and its seed.
        public bool originalHearing; // The host's lobby choice: the Nun hears as in the game alone (else co-op).
        public string scene;
        public int epoch;
        public int peer;
        public int number;
        public float amount;
        public string target;
        public string item;
        public string action;
        public Vector3 position;
        public Vector3 direction;
        public Quaternion rotation;
        public PlayerFrame player;
        public PlayerFrame[] players;
        public Ownership[] owners;
        internal World.WorldSnapshot Parsed; // "world" packets: text already parsed on a network thread.
    }
    /// <summary>The host's lobby choices, the same on every game.</summary>
    [Serializable] public sealed class PlayerFrame
    {
        public int id;
        public string name;
        public string scene;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 look;
        public float health;
        public float height;
        public int state;
        public int lives;
        public string equipped;
        public bool light;
        public bool crouching;
        public bool vent;
        public bool bath; // Still lying in the opening scene's bathtub.
        public string bed;
        public string bedKillPoint;
        public bool body; // False in menus: the player has no character to show.
        public Vector3 velocity;
        public bool downed;      // Knocked down by the Nun; teammates can revive.
        public float bleed;      // Seconds left before a downed player bleeds out.
        public bool spectating;  // Out of lives: watching teammates.
        public int reviving;     // Peer id this player is currently reviving (0 = none).
        public float revive;     // Progress of the revive this player is receiving, 0..1.
        public string character; // Chosen on the lobby's CUSTOMIZE page.
        public bool jumping;     // In the air from a jump.
        public int shots;        // Revolver shots fired so far (teammates see each new one as a recoil).
        public int points;       // Times pointed (key 1): teammates see the arm go out.
        public int whistles;     // Times whistled (key 2): teammates hear it from where they are.
        public int hunting;      // The host's frame: who the Nun is after (her chase music and stingers are loudest for them).
        public Vector3 nunBody;  // The host's frame: the Nun's body collider (height, centre height, radius), for guests' bullets.
        public float lean;       // Peeking round a corner (Q and E): -1 fully left .. 1 fully right.
    }
    [Serializable] public sealed class Ownership
    {
        public string id;
        public int peer;
    }
}
