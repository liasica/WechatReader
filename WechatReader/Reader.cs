﻿using Claunia.PropertyList;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using NSKeyedUnarchiver;
using System.Text.RegularExpressions;
using WechatSharedClasses;

namespace WechatReader
{
    public class Reader : IDisposable
    {
        private SQLiteConnection MMConn;
        private SQLiteConnection WCDBConn;
        private ILocator Locator;

        public Reader(ILocator locator)
        {
            this.Locator = locator;
            MMConn = DBUtil.Connect(locator.Locate(Path.Combine("DB", "MM.sqlite")));
            var wcdbPath = locator.Locate(Path.Combine("DB", "WCDB_Contact.sqlite"));
            if (File.Exists(wcdbPath)) WCDBConn = DBUtil.Connect(wcdbPath);
        }

        public void Dispose()
        {
            if (MMConn != null) MMConn.Dispose();
            if (WCDBConn != null) WCDBConn.Dispose();
        }

        // Get information of the current user as a `Person`.
        public Person GetUser()
        {
            var settingsPath = Locator.Locate("mmsetting.archive");
            var settings = Unarchiver.DeepParse(PropertyListParser.Parse(settingsPath));
            var me = new Person();
            DictUtil.TryGet(settings, "UsrName", out me.UsrName);
            DictUtil.TryGet(settings, "AliasName", out me.Alias);
            DictUtil.TryGet(settings, "NickName", out me.NickName);
            if (DictUtil.TryGetSubclass(settings, "new_dicsetting", out var setting))
            {
                DictUtil.TryGet(setting, "headimgurl", out me.Portrait);
                DictUtil.TryGet(setting, "headhdimgurl", out me.PortraitHD);
            }
            return me;
        }

        // Get a list of friends from `MM.sqlite`.
        public List<Person> GetMMFriends()
        {
            var friends = new List<Person>();
            using (var cmd = new SQLiteCommand(
                "SELECT Friend.UsrName,NickName,ConRemark,ConChatRoomMem,ConStrRes2 FROM Friend JOIN Friend_Ext ON Friend.UsrName=Friend_Ext.UsrName",
                MMConn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var friend = new Person
                    {
                        UsrName = reader.TryGetString(0),
                        NickName = reader.TryGetString(1),
                        ConRemark = reader.TryGetString(2),
                        ConChatRoomMem = reader.TryGetString(3),
                    };
                    friend.TryParseConStrRes2(reader.TryGetString(4));
                    friends.Add(friend);
                }
            }
            return friends;
        }

        // Get a list of friends from `WCDB_Contact.sqlite`.
        public List<Person> GetWCDBFriends()
        {
            var friends = new List<Person>();
            using (var cmd = new SQLiteCommand("SELECT userName,dbContactRemark,dbContactChatRoom,dbContactHeadImage FROM Friend", WCDBConn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var friend = new Person { UsrName = reader.GetString(0) };
                    var sections = reader.TryGetBlobParse(1);
                    if (sections != null)
                    {
                        friend.NickName = BlobParser.TryGetString(sections, 0x0a);
                        friend.Alias = BlobParser.TryGetString(sections, 0x12);
                        friend.ConRemark = BlobParser.TryGetString(sections, 0x1a);
                    }
                    sections = reader.TryGetBlobParse(2);
                    if (sections != null)
                    {
                        friend.DbContactChatRoom = BlobParser.TryGetString(sections, 0x32);
                    }
                    sections = reader.TryGetBlobParse(3);
                    if (sections != null)
                    {
                        friend.Portrait = BlobParser.TryGetString(sections, 0x12);
                        friend.PortraitHD = BlobParser.TryGetString(sections, 0x1a);
                    }
                    friends.Add(friend);
                }
            }
            return friends;
        }

        // Get a list of all friends as `Person`s.
        public List<Person> GetFriends()
        {
            var friends = GetMMFriends();
            if (WCDBConn != null) friends.AddRange(GetWCDBFriends());
            return friends;
        }

        // Get a map of friends from string to `Person`s. Will contain duplicated entries for the same persons.
        // For a `Person`, the keys are: `UsrName`, `MD5(UsrName)`, `Alias`, `MD5(Alias)`.
        public Dictionary<string, Person> GetFriendsDict()
        {
            var dict = new Dictionary<string, Person>();
            foreach (var friend in GetFriends())
            {
                dict[friend.UsrName] = friend;
                dict[Util.CreateMD5(friend.UsrName)] = friend;
                if (!String.IsNullOrEmpty(friend.Alias))
                {
                    dict[friend.Alias] = friend;
                    dict[Util.CreateMD5(friend.Alias)] = friend;
                }
            }
            return dict;
        }

        // Get a list of MD5 of `UsrName`s who have chat records with the user.
        public List<string> GetChatSessions()
        {
            var sessions = new List<string>();
            using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table'", MMConn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = reader.TryGetString(0);
                        var match = Regex.Match(name, @"^Chat_([0-9a-f]{32})$");
                        if (match.Success) sessions.Add(match.Groups[1].Value);
                    }
                }
            }
            return sessions;
        }

        // For a given hash, get all records from this person's chat.
        public List<Record> GetChatRecords(string hash)
        {
            var records = new List<Record>();
            using (var cmd = new SQLiteCommand(
                "SELECT MesLocalID,MesSvrID,CreateTime,Message,Status,ImgStatus,Type,Des FROM Chat_" + hash,
                MMConn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new Record
                        {
                            MesLocalID = reader.TryGetInt(0),
                            MesSvrID = reader.TryGetLong(1),
                            CreateTime = reader.TryGetInt(2),
                            Message = reader.TryGetString(3),
                            Status = reader.TryGetInt(4),
                            ImgStatus = reader.TryGetInt(5),
                            Type = reader.TryGetInt(6),
                            Des = reader.TryGetInt(7)
                        });
                    }
                }
            }
            return records;
        }
    }
}
