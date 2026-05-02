using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public static class ActiveExplicitRefLookupUtility
    {
        public static int Pack(ContentReference content)
            => ((int)content.Kind << 24) | (content.HandleValue & 0x00FFFFFF);

        public static ContentReference Unpack(int key)
        {
            return new ContentReference
            {
                Kind = (ContentReferenceKind)((key >> 24) & 0xFF),
                HandleValue = key & 0x00FFFFFF,
            };
        }
    }
}
