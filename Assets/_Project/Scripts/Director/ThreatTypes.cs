using UnityEngine;
using ProjectFossil.Economy;

namespace ProjectFossil.Director
{
    // Who is paying. The AI director is just another buyer with its own wallet.
    public class ThreatBuyer
    {
        public string         Id     { get; }
        public string         TeamId { get; } // buyers can't target their own team
        public bool           IsAI   { get; }
        public CurrencySystem Wallet { get; }

        public ThreatBuyer(string id, string teamId, bool isAI, CurrencySystem wallet)
        {
            Id     = id;
            TeamId = teamId;
            IsAI   = isAI;
            Wallet = wallet;
        }
    }

    // Who/where the threat is aimed at. Position + team keep it network-friendly; Focus is an optional local hint.
    public struct ThreatTarget
    {
        public string    TeamId;
        public Vector3   Position;
        public Transform Focus;

        public ThreatTarget(string teamId, Vector3 position, Transform focus = null)
        {
            TeamId   = teamId;
            Position = position;
            Focus    = focus;
        }
    }

    public struct ThreatEvent
    {
        public ThreatDefinition Threat;
        public ThreatTarget     Target;
        public ThreatBuyer      Buyer;
        public float            Time;
    }

    public enum PurchaseResult { Success, UnknownThreat, Locked, OnCooldown, CannotAfford, InvalidTarget }
}
