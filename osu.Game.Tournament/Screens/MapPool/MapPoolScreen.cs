// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Tournament.Screens.MapPool
{
    public partial class MapPoolScreen : TournamentMatchScreen
    {
        private FillFlowContainer<FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>> mapFlows = null!;
        private List<TournamentBeatmapPanel> flattenedBeatmapPanels = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        private TeamColour pickColour;
        private ChoiceType pickType;

        private OsuButton buttonRedBan = null!;
        private OsuButton buttonBlueBan = null!;
        private OsuButton buttonRedPick = null!;
        private OsuButton buttonBluePick = null!;
        private OsuButton buttonPurplePick = null!;

        private ScheduledDelegate? scheduledScreenChange;

        public IBeatmapInfo? lastSelectedMap { get; set; }

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            flattenedBeatmapPanels = new List<TournamentBeatmapPanel>();
            InternalChildren = new Drawable[]
            {
                new TourneyVideo("mappool")
                {
                    Loop = true,
                    RelativeSizeAxes = Axes.Both,
                },
                new MatchHeader
                {
                    ShowScores = true,
                },
                mapFlows = new FillFlowContainer<FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>>
                {
                    Y = 160,
                    Spacing = new Vector2(10, 10),
                    Direction = FillDirection.Vertical,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new ControlPanel
                {
                    Children = new Drawable[]
                    {
                        new TournamentSpriteText
                        {
                            Text = "Current Mode"
                        },
                        buttonRedBan = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Red Ban",
                            Action = () => setMode(TeamColour.Red, ChoiceType.Ban)
                        },
                        buttonBlueBan = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Blue Ban",
                            Action = () => setMode(TeamColour.Blue, ChoiceType.Ban)
                        },
                        buttonRedPick = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Red Pick",
                            Action = () => setMode(TeamColour.Red, ChoiceType.Pick)
                        },
                        buttonBluePick = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Blue Pick",
                            Action = () => setMode(TeamColour.Blue, ChoiceType.Pick)
                        },
                        buttonPurplePick = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Purple Pick",
                            Action = () => setMode(TeamColour.Purple, ChoiceType.Pick)
                        },
                        new ControlPanel.Spacer(),
                        new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Reset",
                            Action = reset
                        },
                        new ControlPanel.Spacer(),
                        new OsuCheckbox
                        {
                            LabelText = "Split display by mods",
                            Current = LadderInfo.SplitMapPoolByMods,
                        },
                    },
                }
            };

            ipc.Beatmap.BindValueChanged(beatmapChanged);
        }

        private Bindable<bool>? splitMapPoolByMods;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            splitMapPoolByMods = LadderInfo.SplitMapPoolByMods.GetBoundCopy();
            splitMapPoolByMods.BindValueChanged(_ => updateDisplay());
        }

        private void beatmapChanged(ValueChangedEvent<TournamentBeatmap?> beatmap)
        {
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            int totalBansRequired = CurrentMatch.Value.Round.Value.BanCount.Value * 2;

            if (CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) < totalBansRequired)
                return;

            // if bans have already been placed, beatmap changes result in a selection being made automatically
            if (beatmap.NewValue?.OnlineID > 0)
                addForBeatmap(beatmap.NewValue);
        }

        private void setMode(TeamColour colour, ChoiceType choiceType)
        {
            pickColour = colour;
            pickType = choiceType;

            buttonRedBan.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Ban);
            buttonBlueBan.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Ban);
            buttonRedPick.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Pick);
            buttonBluePick.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Pick);
            buttonPurplePick.Colour = setColour(pickColour == TeamColour.Purple && pickType == ChoiceType.Pick);

            static Color4 setColour(bool active) => active ? Color4.White : Color4.Gray;
        }

        private void setNextMode()
        {
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            int totalBansRequired = CurrentMatch.Value.Round.Value.BanCount.Value * 2;

            TeamColour lastPickColour = CurrentMatch.Value.PicksBans.LastOrDefault()?.Team ?? TeamColour.Red;

            TeamColour nextColour;

            bool hasAllBans = CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) >= totalBansRequired;

            if (!hasAllBans)
            {
                // Ban phase: switch teams every second ban.
                nextColour = CurrentMatch.Value.PicksBans.Count % 2 == 1
                    ? getOppositeTeamColour(lastPickColour)
                    : lastPickColour;
            }
            else
            {
                // Pick phase : switch teams every pick, except for the first pick which generally goes to the team that placed the last ban.
                nextColour = pickType == ChoiceType.Pick
                    ? getOppositeTeamColour(lastPickColour)
                    : lastPickColour;
            }

            setMode(nextColour, hasAllBans ? ChoiceType.Pick : ChoiceType.Ban);

            TeamColour getOppositeTeamColour(TeamColour colour) => colour == TeamColour.Red ? TeamColour.Blue : TeamColour.Red;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            var map = flattenedBeatmapPanels.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition));

            if (map != null)
            {
                if (e.Button == MouseButton.Left && map.Beatmap?.OnlineID > 0)
                    addForBeatmap(map.Beatmap);
                else
                {
                    var existing = CurrentMatch.Value?.PicksBans.FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID);

                    if (existing != null)
                    {
                        CurrentMatch.Value?.PicksBans.Remove(existing);
                        setNextMode();
                    }
                }

                return true;
            }

            return base.OnMouseDown(e);
        }

        private void reset()
        {
            CurrentMatch.Value?.PicksBans.Clear();
            setNextMode();
        }

        private void addForBeatmap(IBeatmapInfo beatmapInfo)
        {
            lastSelectedMap = beatmapInfo;
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            if (CurrentMatch.Value.Round.Value.RoundGroups.All(rg => rg.Beatmaps.All(b => b.Beatmap?.OnlineID != beatmapInfo.OnlineID)))
                // don't attempt to add if the beatmap isn't in our pool
                return;

            if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapInfo.OnlineID))
                // don't attempt to add if already exists.
                return;

            CurrentMatch.Value.PicksBans.Add(new BeatmapChoice
            {
                Team = pickColour,
                Type = pickType,
                BeatmapID = beatmapInfo.OnlineID
            });

            setNextMode();

            if (LadderInfo.AutoProgressScreens.Value)
            {
                if (pickType == ChoiceType.Pick && CurrentMatch.Value.PicksBans.Any(i => i.Type == ChoiceType.Pick))
                {
                    scheduledScreenChange?.Cancel();
                    scheduledScreenChange = Scheduler.AddDelayed(() => { sceneManager?.SetScreen(typeof(GameplayScreen)); }, 10000);
                }
            }
        }

        public override void Hide()
        {
            scheduledScreenChange?.Cancel();
            base.Hide();
        }

        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);
            updateDisplay();
        }

        private void updateDisplay()
        {
            mapFlows.Clear();
            flattenedBeatmapPanels.Clear();

            if (CurrentMatch.Value == null)
                return;

            int totalRows = 0;

            if (CurrentMatch.Value.Round.Value != null)
            {

                foreach (var rg in CurrentMatch.Value.Round.Value.RoundGroups)
                {
                    var currentFlowGroup = new FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>
                    {
                        Spacing = new Vector2(10, 5),
                        Direction = FillDirection.Full,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y
                    };
                    mapFlows.Add(currentFlowGroup);

                    FillFlowContainer<TournamentBeatmapPanel>? currentFlow = null;
                    string? currentMods = null;
                    int flowCount = 0;

                    foreach (var b in rg.Beatmaps)
                    {
                        if (currentFlow == null || (LadderInfo.SplitMapPoolByMods.Value && currentMods != b.Mods))
                        {
                            currentFlowGroup.Add(currentFlow = new FillFlowContainer<TournamentBeatmapPanel>
                            {
                                Spacing = new Vector2(10, 5),
                                Direction = FillDirection.Full,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y
                            });

                            currentMods = b.Mods;

                            totalRows++;
                            flowCount = 0;
                        }

                        if (++flowCount > 2)
                        {
                            totalRows++;
                            flowCount = 1;
                        }

                        TournamentBeatmapPanel panel = new TournamentBeatmapPanel(b.Beatmap, b.Mods)
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Height = 42,
                        };
                        currentFlow.Add(panel);
                        flattenedBeatmapPanels.Add(panel);
                    }
                }
            }

            mapFlows.Padding = new MarginPadding(5)
            {
                // remove horizontal padding to increase flow width to 3 panels
                Horizontal = totalRows > 9 ? 0 : 100
            };
        }
    }
}
