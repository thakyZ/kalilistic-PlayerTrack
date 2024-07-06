﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dalamud.DrunkenToad.Core;
using Dalamud.DrunkenToad.Core.Models;
using Dalamud.DrunkenToad.Extensions;
using PlayerTrack.Domain;
using PlayerTrack.Models;

namespace PlayerTrack.Plugin;

public static class EventDispatcher
{
    private static readonly Channel<Action> EventChannel = Channel.CreateBounded<Action>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
    });

    private static readonly CancellationTokenSource Cts = new();

    public static void Start()
    {
        DalamudContext.PluginLog.Verbose("Entering EventDispatcher.Start()");
        Task.Run(() => ProcessChannelAsync(EventChannel.Reader, Cts.Token));
        DalamudContext.PlayerLocationManager.LocationStarted += OnStartLocation;
        DalamudContext.PlayerLocationManager.LocationEnded += OnEndLocation;
        DalamudContext.PlayerEventDispatcher.AddPlayers += OnAddPlayers;
        DalamudContext.PlayerEventDispatcher.RemovePlayers += OnRemovePlayers;
        ContextMenuHandler.SelectPlayer += OnSelectPlayer;
        DalamudContext.ClientStateHandler.Login += OnLogin;
        if (DalamudContext.ClientStateHandler.IsLoggedIn) OnLogin();
        DalamudContext.SocialListHandler.FriendListReceived += OnFriendListReceived;
        DalamudContext.SocialListHandler.FreeCompanyReceived += OnFreeCompanyReceived;
        DalamudContext.SocialListHandler.BlackListReceived += OnBlackListReceived;
        DalamudContext.SocialListHandler.LinkShellReceived += OnLinkShellReceived;
        DalamudContext.SocialListHandler.CrossWorldLinkShellReceived += OnCrossWorldLinkShellReceived;
    }

    public static void Dispose()
    {
        DalamudContext.PluginLog.Verbose("Entering EventDispatcher.Dispose()");
        try
        {
            DalamudContext.SocialListHandler.FriendListReceived -= OnFriendListReceived;
            DalamudContext.SocialListHandler.FreeCompanyReceived -= OnFreeCompanyReceived;
            DalamudContext.SocialListHandler.BlackListReceived -= OnBlackListReceived;
            DalamudContext.SocialListHandler.LinkShellReceived -= OnLinkShellReceived;
            DalamudContext.SocialListHandler.CrossWorldLinkShellReceived -= OnCrossWorldLinkShellReceived;
            DalamudContext.ClientStateHandler.Login -= OnLogin;
            DalamudContext.PlayerLocationManager.LocationStarted -= OnStartLocation;
            DalamudContext.PlayerLocationManager.LocationEnded -= OnEndLocation;
            DalamudContext.PlayerEventDispatcher.AddPlayers -= OnAddPlayers;
            DalamudContext.PlayerEventDispatcher.RemovePlayers -= OnRemovePlayers;
            ContextMenuHandler.SelectPlayer -= OnSelectPlayer;
            Cts.Cancel();
            EventChannel.Writer.Complete();
        }
        catch (Exception ex)
        {
            DalamudContext.PluginLog.Error(ex, "Failed to dispose EventDispatcher properly.");
        }
    }

    private static async Task ProcessChannelAsync(ChannelReader<Action> reader, CancellationToken token)
    {
        DalamudContext.PluginLog.Verbose("Entering EventDispatcher.ProcessChannelAsync()");
        await foreach (var action in reader.ReadAllAsync(token))
        {
            action();
        }
    }

    private static void OnStartLocation(ToadLocation location) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnStartLocation(): {location.GetName()}");
        ServiceContext.EncounterService.StartCurrentEncounter(location);
    });

    private static void OnEndLocation(ToadLocation location) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnEndLocation(): {location.GetName()}");
        ServiceContext.EncounterService.EndCurrentEncounter();
    });

    private static void OnAddPlayers(List<ToadPlayer> players) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnAddPlayers(): {players.Count}");
        foreach (var player in players)
        {
            ServiceContext.PlayerProcessService.AddOrUpdatePlayer(player);
        }
    });

    private static void OnRemovePlayers(List<ulong> playerContentIds) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnRemovePlayers(): {playerContentIds.Count}");
        foreach (var playerContentId in playerContentIds)
        {
            ServiceContext.PlayerProcessService.RemoveCurrentPlayer(playerContentId);
        }
    });

    private static void OnSelectPlayer(ToadPlayer toadPlayer, bool isCurrent) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnSelectPlayer(): {toadPlayer.Name}");
        ServiceContext.PlayerProcessService.AddOrUpdatePlayer(toadPlayer, isCurrent, true);
    });
    
    private static void OnLogin() => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnLogin()");
        LocalPlayerService.AddOrUpdateLocalPlayer(DalamudContext.ClientStateHandler.GetLocalPlayer());
    });

    private static void OnFriendListReceived(List<ToadSocialListMember> members) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnFriendListReceived()");
        SocialListService.HandleMembersList(SocialListType.FriendList, members);
    });
    
    private static void OnCrossWorldLinkShellReceived(byte index, List<ToadSocialListMember> members) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnCrossWorldLinkShellReceived()");
        SocialListService.HandleMembersList(SocialListType.CrossWorldLinkShell, members, (ushort)(index + 1));
    });

    private static void OnLinkShellReceived(byte index, List<ToadSocialListMember> members) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnLinkShellReceived()");
        SocialListService.HandleMembersList(SocialListType.LinkShell, members, (ushort)(index + 1));
    });

    private static void OnBlackListReceived(List<ToadSocialListMember> members) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnBlackListReceived()");
        SocialListService.HandleMembersList(SocialListType.BlackList, members);
    });
    
    private static void OnFreeCompanyReceived(byte pageCount, byte currentPage, List<ToadSocialListMember> members) => EventChannel.Writer.TryWrite(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering EventDispatcher.OnFreeCompanyReceived(): {currentPage}/{pageCount}: {members.Count}");
        SocialListService.HandleMembersList(SocialListType.FreeCompany, members, 0, currentPage, pageCount);
    });
}
