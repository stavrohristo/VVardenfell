using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    internal static class ShellDiseaseMessageBoxUtility
    {
        public static void QueueContractDiseaseMessage(
            DynamicBuffer<ShellMessageBoxRequest> messageBoxes,
            string contractDiseaseMessage,
            ref RuntimeSpellDefBlob spell)
        {
            string spellId = spell.Id.ToString();
            string blobName = spell.Name.ToString();
            string spellName = string.IsNullOrWhiteSpace(blobName) ? spellId : blobName.Trim();
            messageBoxes.Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(FormatDiseaseMessage(contractDiseaseMessage, spellName)),
            });
        }

        static string FormatDiseaseMessage(string message, string spellName)
        {
            if (string.IsNullOrEmpty(message))
                return spellName;

            int placeholder = message.IndexOf("%s", StringComparison.Ordinal);
            return placeholder >= 0
                ? message.Substring(0, placeholder) + spellName + message.Substring(placeholder + 2)
                : message + " " + spellName;
        }
    }
}
