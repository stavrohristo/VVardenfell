using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Combat;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class BattleSimulatorWindowView : MonoBehaviour
    {
        public readonly struct CatalogEntry
        {
            public readonly ActorDefHandle Actor;
            public readonly string Id;
            public readonly string Label;

            public CatalogEntry(ActorDefHandle actor, string id, string label)
            {
                Actor = actor;
                Id = id ?? string.Empty;
                Label = label ?? string.Empty;
            }
        }

        public readonly struct RosterEntry
        {
            public readonly ActorDefHandle Actor;
            public readonly int Count;

            public RosterEntry(ActorDefHandle actor, int count)
            {
                Actor = actor;
                Count = count;
            }
        }

        sealed class RosterRow
        {
            public int SelectedCatalogIndex;
            public string Search;
            public string CountText;

            public RosterRow(int selectedCatalogIndex, int count, CatalogEntry[] catalog)
            {
                SelectedCatalogIndex = selectedCatalogIndex;
                Search = (uint)selectedCatalogIndex < (uint)catalog.Length ? catalog[selectedCatalogIndex].Label : string.Empty;
                CountText = math.max(1, count).ToString();
            }
        }

        const int MaxResultsPerRow = 8;
        const float SetupWidth = 1120f;
        const float SetupHeight = 720f;
        const float SetupMargin = 12f;
        const float MinPaneWidth = 360f;
        const float MinPaneHeight = 220f;
        const float OverlayWidth = 760f;
        const float OverlayHeight = 138f;

        static readonly Color WindowColor = new(0.12f, 0.10f, 0.08f, 0.96f);
        static readonly Color PaneColor = new(0.05f, 0.045f, 0.035f, 0.92f);
        static readonly Color TextColor = new(0.94f, 0.85f, 0.68f, 1f);
        static readonly Color DimTextColor = new(0.74f, 0.70f, 0.62f, 1f);
        static readonly Color ErrorTextColor = new(0.95f, 0.48f, 0.38f, 1f);
        static readonly Color GroupAColor = new(0.50f, 0.12f, 0.10f, 1f);
        static readonly Color GroupBColor = new(0.12f, 0.22f, 0.48f, 1f);
        static readonly Color BarBackColor = new(0.03f, 0.025f, 0.02f, 0.92f);
        static readonly Color BarFrameColor = new(0.66f, 0.56f, 0.38f, 1f);

        CatalogEntry[] _catalog = Array.Empty<CatalogEntry>();
        readonly List<RosterRow> _groupA = new();
        readonly List<RosterRow> _groupB = new();
        Vector2 _scrollA;
        Vector2 _scrollB;
        BattleSimulatorState _state;
        float _elapsedTime;
        Action<RosterEntry[], RosterEntry[]> _onReady;
        Action _onReset;
        GUIStyle _windowStyle;
        GUIStyle _paneStyle;
        GUIStyle _labelStyle;
        GUIStyle _headerStyle;
        GUIStyle _dimStyle;
        GUIStyle _errorStyle;
        GUIStyle _statusStyle;
        GUIStyle _buttonStyle;
        GUIStyle _textFieldStyle;
        GUIStyle _overlayStyle;
        GUIStyle _overlayTeamStyle;
        GUIStyle _overlayCountStyle;
        GUIStyle _vsStyle;
        GUIStyle _timerStyle;
        GUIStyle _winnerStyle;
        string _validationError = string.Empty;
        bool _stylesReady;

        public static BattleSimulatorWindowView Create(
            CatalogEntry[] catalog,
            int2 battlegroundCell,
            Action<RosterEntry[], RosterEntry[]> onReady,
            Action onReset)
        {
            var go = new GameObject("VVardenfell.BattleSimulatorIMGUI");
            var view = go.AddComponent<BattleSimulatorWindowView>();
            view.Initialize(catalog, battlegroundCell, onReady, onReset);
            return view;
        }

        public void SetState(in BattleSimulatorState state, float elapsedTime)
        {
            _state = state;
            _elapsedTime = elapsedTime;
        }

        void Initialize(
            CatalogEntry[] catalog,
            int2 battlegroundCell,
            Action<RosterEntry[], RosterEntry[]> onReady,
            Action onReset)
        {
            _catalog = catalog ?? Array.Empty<CatalogEntry>();
            _onReady = onReady;
            _onReset = onReset;
            _state = new BattleSimulatorState
            {
                BattlegroundCell = battlegroundCell,
                Phase = (byte)BattleSimulatorPhase.Setup,
            };

            int groupAIndex = _catalog.Length > 0 ? 0 : -1;
            int groupBIndex = _catalog.Length > 1 ? 1 : groupAIndex;
            _groupA.Add(new RosterRow(groupAIndex, 250, _catalog));
            _groupB.Add(new RosterRow(groupBIndex, 250, _catalog));
        }

        void OnGUI()
        {
            EnsureStyles();

            if (_state.Phase == (byte)BattleSimulatorPhase.Running
                || _state.Phase == (byte)BattleSimulatorPhase.Complete)
            {
                DrawBattleOverlay();
            }

            if (_state.Phase == (byte)BattleSimulatorPhase.Setup
                || _state.Phase == (byte)BattleSimulatorPhase.Complete)
            {
                DrawSetupPanel();
            }
        }

        void DrawSetupPanel()
        {
            Rect rect = ClampedCenteredRect(SetupWidth, SetupHeight, SetupMargin);
            using (new GuiColorScope(WindowColor))
                GUI.Box(rect, GUIContent.none, _windowStyle);

            GUILayout.BeginArea(new Rect(rect.x + 18f, rect.y + 14f, rect.width - 36f, rect.height - 28f));
            GUILayout.Label("Battle Setup", _headerStyle);
            DrawStatusStrip();
            GUILayout.Space(8f);

            float contentWidth = rect.width - 36f;
            float footerHeight = 52f + (string.IsNullOrWhiteSpace(_validationError) ? 0f : 24f);
            float paneAreaHeight = math.max(MinPaneHeight, rect.height - 150f - footerHeight);
            bool splitPanes = contentWidth >= MinPaneWidth * 2f + 20f;
            if (splitPanes)
            {
                float paneWidth = (contentWidth - 12f) * 0.5f;
                GUILayout.BeginHorizontal();
                DrawTeamPane("Group A", _groupA, ref _scrollA, paneWidth, paneAreaHeight);
                GUILayout.Space(12f);
                DrawTeamPane("Group B", _groupB, ref _scrollB, paneWidth, paneAreaHeight);
                GUILayout.EndHorizontal();
            }
            else
            {
                float paneHeight = math.max(MinPaneHeight, paneAreaHeight * 0.5f - 6f);
                DrawTeamPane("Group A", _groupA, ref _scrollA, contentWidth, paneHeight);
                GUILayout.Space(8f);
                DrawTeamPane("Group B", _groupB, ref _scrollB, contentWidth, paneHeight);
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Total A: {Total(_groupA)}", _labelStyle, GUILayout.Width(160f));
            GUILayout.Label($"Total B: {Total(_groupB)}", _labelStyle, GUILayout.Width(160f));
            GUILayout.FlexibleSpace();
            if (_state.Phase == (byte)BattleSimulatorPhase.Complete && GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(120f), GUILayout.Height(34f)))
                _onReset?.Invoke();
            bool canReady = CanSubmitReady();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && canReady;
            if (GUILayout.Button("Ready", _buttonStyle, GUILayout.Width(160f), GUILayout.Height(34f)))
                SubmitReady();
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_validationError))
                GUILayout.Label(_validationError, _errorStyle, GUILayout.Height(24f));

            GUILayout.EndArea();
        }

        void DrawStatusStrip()
        {
            GUILayout.BeginVertical(_paneStyle);
            if (!_state.Status.IsEmpty)
                GUILayout.Label(_state.Status.ToString(), _dimStyle);
            GUILayout.EndVertical();
        }

        void DrawTeamPane(string label, List<RosterRow> rows, ref Vector2 scroll, float width, float height)
        {
            GUILayout.BeginVertical(_paneStyle, GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Unit", _buttonStyle, GUILayout.Width(110f), GUILayout.Height(28f)))
                rows.Add(new RosterRow(_catalog.Length > 0 ? 0 : -1, 1, _catalog));
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, false, true);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRosterRow(label, rows, i, width - 34f);
                GUILayout.Space(8f);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void DrawRosterRow(string teamLabel, List<RosterRow> rows, int rowIndex, float rowWidth)
        {
            RosterRow row = rows[rowIndex];
            float searchWidth = math.max(110f, rowWidth - 280f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Actor", _labelStyle, GUILayout.Width(42f));
            string newSearch = GUILayout.TextField(row.Search ?? string.Empty, _textFieldStyle, GUILayout.Width(searchWidth), GUILayout.Height(24f));
            if (!string.Equals(newSearch, row.Search, StringComparison.Ordinal))
            {
                row.Search = newSearch;
                row.SelectedCatalogIndex = ExactCatalogMatch(newSearch);
            }

            GUILayout.Label("Count", _labelStyle, GUILayout.Width(46f));
            row.CountText = GUILayout.TextField(row.CountText ?? "1", _textFieldStyle, GUILayout.Width(54f), GUILayout.Height(24f));
            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(28f), GUILayout.Height(24f)))
                row.CountText = math.max(1, ResolveCount(row) - 1).ToString();
            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(28f), GUILayout.Height(24f)))
                row.CountText = (ResolveCount(row) + 1).ToString();
            if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(28f), GUILayout.Height(24f)))
            {
                if (rows.Count <= 1)
                    _validationError = $"{teamLabel} must keep at least one roster row.";
                else
                    rows.RemoveAt(rowIndex);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            string selected = (uint)row.SelectedCatalogIndex < (uint)_catalog.Length
                ? _catalog[row.SelectedCatalogIndex].Id
                : "No actor selected";
            GUILayout.Label(selected, _dimStyle);

            int shown = 0;
            for (int i = 0; i < _catalog.Length && shown < MaxResultsPerRow; i++)
            {
                if (!Matches(_catalog[i], row.Search))
                    continue;

                if (GUILayout.Button(_catalog[i].Label, _buttonStyle, GUILayout.Height(22f)))
                {
                    row.SelectedCatalogIndex = i;
                    row.Search = _catalog[i].Label;
                    _validationError = string.Empty;
                }
                shown++;
            }

            if (shown == 0)
                GUILayout.Label("No matching actors.", _errorStyle);
            GUILayout.EndVertical();
        }

        void DrawBattleOverlay()
        {
            Rect rect = new((Screen.width - OverlayWidth) * 0.5f, 18f, OverlayWidth, OverlayHeight);
            float elapsed = _state.StartedAt > 0f
                ? math.max(0f, (_state.Phase == (byte)BattleSimulatorPhase.Complete ? _state.CompletedAt : _elapsedTime) - _state.StartedAt)
                : 0f;

            Rect inner = new(rect.x + 14f, rect.y + 12f, rect.width - 28f, rect.height - 24f);
            Rect left = new(inner.x, inner.y + 18f, 285f, 76f);
            Rect right = new(inner.xMax - 285f, inner.y + 18f, 285f, 76f);
            Rect center = new(left.xMax + 10f, inner.y, right.x - left.xMax - 20f, inner.height);

            DrawTeamScorePanel(left, "GROUP A", _state.GroupAAlive, _state.GroupATotal, GroupAColor, alignRight: false);
            DrawTeamScorePanel(right, "GROUP B", _state.GroupBAlive, _state.GroupBTotal, GroupBColor, alignRight: true);

            GUI.Label(new Rect(center.x, center.y + 8f, center.width, 42f), "VS", _vsStyle);
            GUI.Label(new Rect(center.x, center.y + 55f, center.width, 24f), FormatTimer(elapsed), _timerStyle);

            if (_state.Phase == (byte)BattleSimulatorPhase.Complete)
                GUI.Label(new Rect(rect.x + 16f, rect.yMax - 34f, rect.width - 32f, 26f), WinnerText(), _winnerStyle);

            if (_state.Phase == (byte)BattleSimulatorPhase.Running)
            {
                Rect resetRect = new(rect.xMax + 12f, rect.y + 52f, 96f, 32f);
                if (GUI.Button(resetRect, "Reset", _buttonStyle))
                    _onReset?.Invoke();
            }
        }

        void DrawTeamScorePanel(Rect rect, string label, int alive, int total, Color accent, bool alignRight)
        {
            using (new GuiColorScope(PaneColor))
                GUI.Box(rect, GUIContent.none, _overlayStyle);

            Rect accentRect = alignRight
                ? new Rect(rect.xMax - 6f, rect.y + 6f, 4f, rect.height - 12f)
                : new Rect(rect.x + 2f, rect.y + 6f, 4f, rect.height - 12f);
            DrawSolidRect(accentRect, accent);

            Rect labelRect = new(rect.x + 16f, rect.y + 10f, rect.width - 32f, 22f);
            Rect countRect = new(rect.x + 16f, rect.y + 28f, rect.width - 32f, 30f);
            Rect barRect = new(rect.x + 16f, rect.y + 60f, rect.width - 32f, 8f);

            _overlayTeamStyle.alignment = alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            _overlayCountStyle.alignment = alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            GUI.Label(labelRect, label, _overlayTeamStyle);
            GUI.Label(countRect, $"{math.max(0, alive)} / {math.max(0, total)}", _overlayCountStyle);
            DrawAliveBar(barRect, alive, total, accent);
        }

        static void DrawAliveBar(Rect rect, int alive, int total, Color fill)
        {
            DrawSolidRect(rect, BarFrameColor);
            Rect inner = new(rect.x + 1f, rect.y + 1f, math.max(0f, rect.width - 2f), math.max(0f, rect.height - 2f));
            DrawSolidRect(inner, BarBackColor);
            float ratio = total > 0 ? math.saturate((float)alive / total) : 0f;
            DrawSolidRect(new Rect(inner.x, inner.y, inner.width * ratio, inner.height), fill);
        }

        static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static string FormatTimer(float elapsed)
        {
            int totalSeconds = math.max(0, (int)math.floor(elapsed));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds - minutes * 60;
            return $"{minutes:00}:{seconds:00}";
        }

        void SubmitReady()
        {
            if (_state.Phase != (byte)BattleSimulatorPhase.Setup
                && _state.Phase != (byte)BattleSimulatorPhase.Complete)
            {
                _validationError = "Ready is only available from setup.";
                return;
            }

            if (_catalog.Length == 0)
            {
                _validationError = "No spawnable actors are available in the baked runtime content.";
                return;
            }

            if (!TryBuildRoster("Group A", _groupA, out RosterEntry[] groupA)
                || !TryBuildRoster("Group B", _groupB, out RosterEntry[] groupB))
            {
                return;
            }

            _validationError = string.Empty;
            _onReady?.Invoke(groupA, groupB);
        }

        bool CanSubmitReady()
        {
            if (_state.Phase != (byte)BattleSimulatorPhase.Setup
                && _state.Phase != (byte)BattleSimulatorPhase.Complete)
                return false;
            if (_catalog.Length == 0)
                return false;
            return IsRosterValid(_groupA) && IsRosterValid(_groupB);
        }

        bool IsRosterValid(List<RosterRow> rows)
        {
            if (rows.Count == 0)
                return false;

            for (int i = 0; i < rows.Count; i++)
            {
                if ((uint)rows[i].SelectedCatalogIndex >= (uint)_catalog.Length)
                    return false;
                if (ResolveCount(rows[i]) <= 0)
                    return false;
            }

            return true;
        }

        bool TryBuildRoster(string label, List<RosterRow> rows, out RosterEntry[] roster)
        {
            var result = new List<RosterEntry>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                RosterRow row = rows[i];
                if ((uint)row.SelectedCatalogIndex >= (uint)_catalog.Length)
                {
                    _validationError = $"{label} row {i + 1} has no selected actor.";
                    roster = Array.Empty<RosterEntry>();
                    return false;
                }

                int count = ResolveCount(row);
                if (count <= 0)
                {
                    _validationError = $"{label} row {i + 1} count must be positive.";
                    roster = Array.Empty<RosterEntry>();
                    return false;
                }

                result.Add(new RosterEntry(_catalog[row.SelectedCatalogIndex].Actor, count));
            }

            roster = result.ToArray();
            return true;
        }

        int ExactCatalogMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return -1;

            for (int i = 0; i < _catalog.Length; i++)
            {
                if (string.Equals(_catalog[i].Label, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_catalog[i].Id, value, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static bool Matches(CatalogEntry entry, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return entry.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || entry.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int ResolveCount(RosterRow row)
        {
            if (!int.TryParse(row.CountText, out int value))
                return 0;
            return math.max(1, value);
        }

        static int Total(List<RosterRow> rows)
        {
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
                total += ResolveCount(rows[i]);
            return total;
        }

        string WinnerText()
        {
            return _state.WinningTeam switch
            {
                (byte)BattleSimulatorTeamId.GroupA => "Group A Wins",
                (byte)BattleSimulatorTeamId.GroupB => "Group B Wins",
                _ => "Draw",
            };
        }

        static Rect ClampedCenteredRect(float width, float height, float margin)
        {
            float resolvedWidth = math.max(320f, math.min(width, Screen.width - margin * 2f));
            float resolvedHeight = math.max(360f, math.min(height, Screen.height - margin * 2f));
            return new Rect((Screen.width - resolvedWidth) * 0.5f, (Screen.height - resolvedHeight) * 0.5f, resolvedWidth, resolvedHeight);
        }

        void EnsureStyles()
        {
            if (_stylesReady)
                return;

            _windowStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 12, 12) };
            _paneStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10),
                normal = { background = TextureFor(PaneColor) },
            };
            _overlayStyle = new GUIStyle(GUI.skin.box) { normal = { background = TextureFor(WindowColor) } };
            _labelStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 14 };
            _headerStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 18, fontStyle = FontStyle.Bold };
            _dimStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = DimTextColor }, fontSize = 12 };
            _errorStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = ErrorTextColor }, fontSize = 13 };
            _statusStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 13, fontStyle = FontStyle.Bold };
            _buttonStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = TextColor }, fontSize = 13 };
            _textFieldStyle = new GUIStyle(GUI.skin.textField) { normal = { textColor = TextColor }, fontSize = 13 };
            _overlayTeamStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = DimTextColor }, fontSize = 13, fontStyle = FontStyle.Bold };
            _overlayCountStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 28, fontStyle = FontStyle.Bold };
            _vsStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _timerStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = DimTextColor }, fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _winnerStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = TextColor }, fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _stylesReady = true;
        }

        static Texture2D TextureFor(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        readonly struct GuiColorScope : IDisposable
        {
            readonly Color _previous;

            public GuiColorScope(Color color)
            {
                _previous = GUI.color;
                GUI.color = color;
            }

            public void Dispose()
            {
                GUI.color = _previous;
            }
        }
    }
}
