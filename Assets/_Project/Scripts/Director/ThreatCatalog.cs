using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Director
{
    [CreateAssetMenu(menuName = "Project Fossil/Threat Catalog", fileName = "ThreatCatalog")]
    public class ThreatCatalog : ScriptableObject
    {
        public List<ThreatDefinition> threats = new List<ThreatDefinition>();
    }
}
