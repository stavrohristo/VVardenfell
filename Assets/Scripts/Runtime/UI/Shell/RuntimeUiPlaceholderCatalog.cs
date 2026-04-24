namespace VVardenfell.Runtime.UI.Shell
{
    public enum RuntimeUiPlaceholderId : byte
    {
        None = 0,
        InventoryAvatarPreview = 1,
        // Stats placeholder ids (2, 3, 4) removed when the Stats window was rebuilt against
        // the vanilla reference. The window now renders real value rows (empty until the
        // actor data pillar is wired) instead of dev-commentary placeholder bodies.
        SpellEffectsPanel = 5,
        SpellListPanel = 6,
        MapViewportPanel = 7,
    }

    public readonly struct RuntimeUiPlaceholderDescriptor
    {
        public RuntimeUiPlaceholderDescriptor(RuntimeUiPlaceholderId id, string title, string body)
        {
            Id = id;
            Title = title;
            Body = body;
        }

        public RuntimeUiPlaceholderId Id { get; }
        public string Title { get; }
        public string Body { get; }
    }

    public static class RuntimeUiPlaceholderCatalog
    {
        public static RuntimeUiPlaceholderDescriptor Describe(RuntimeUiPlaceholderId id)
        {
            return id switch
            {
                RuntimeUiPlaceholderId.InventoryAvatarPreview => new RuntimeUiPlaceholderDescriptor(
                    id,
                    "Avatar Preview",
                    "Equipment paper doll and actor preview are unavailable in the current runtime slice."),
                RuntimeUiPlaceholderId.SpellEffectsPanel => new RuntimeUiPlaceholderDescriptor(
                    id,
                    "Effects",
                    "Active effects and enchant summaries are not yet exposed by the runtime shell."),
                RuntimeUiPlaceholderId.SpellListPanel => new RuntimeUiPlaceholderDescriptor(
                    id,
                    "Spellbook",
                    "Known spells, powers, and enchanted item casting will appear here after spell state is wired."),
                RuntimeUiPlaceholderId.MapViewportPanel => new RuntimeUiPlaceholderDescriptor(
                    id,
                    "Map",
                    "Local and world map rendering are reserved for a later runtime map pass."),
                _ => new RuntimeUiPlaceholderDescriptor(
                    RuntimeUiPlaceholderId.None,
                    "Unavailable",
                    "This surface is unavailable in the current runtime slice."),
            };
        }
    }
}
