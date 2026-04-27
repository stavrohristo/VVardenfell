using System.Collections.Generic;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Bootstrap
{
    struct PreviewRecords
    {
        public ActorDef Actor;
        public RaceDef Race;
        public ActorBodyPartDef[] BodyParts;
        public Dictionary<string, ActorBodyPartDef> BodyPartsById;
        public bool IsFemale;
        public bool IsBeast;
    }
}
