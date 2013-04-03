﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Sqlite
{
    /// <summary>
    /// Class SQLiteUserDataRepository
    /// </summary>
    public class SQLiteUserDataRepository : SqliteRepository, IUserDataRepository
    {
        /// <summary>
        /// The repository name
        /// </summary>
        public const string RepositoryName = "SQLite";

        /// <summary>
        /// Gets the name of the repository
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return RepositoryName;
            }
        }

        /// <summary>
        /// The _protobuf serializer
        /// </summary>
        private readonly IProtobufSerializer _protobufSerializer;

        /// <summary>
        /// The _app paths
        /// </summary>
        private readonly IApplicationPaths _appPaths;

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteUserDataRepository" /> class.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="protobufSerializer">The protobuf serializer.</param>
        /// <param name="logManager">The log manager.</param>
        /// <exception cref="System.ArgumentNullException">protobufSerializer</exception>
        public SQLiteUserDataRepository(IApplicationPaths appPaths, IProtobufSerializer protobufSerializer, ILogManager logManager)
            : base(logManager)
        {
            if (protobufSerializer == null)
            {
                throw new ArgumentNullException("protobufSerializer");
            }
            if (appPaths == null)
            {
                throw new ArgumentNullException("appPaths");
            }

            _protobufSerializer = protobufSerializer;
            _appPaths = appPaths;
        }

        /// <summary>
        /// Opens the connection to the database
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Initialize()
        {
            var dbFile = Path.Combine(_appPaths.DataPath, "userdata.db");

            await ConnectToDB(dbFile).ConfigureAwait(false);

            string[] queries = {

                                "create table if not exists userdata (id GUID, userId GUID, data BLOB)",
                                "create unique index if not exists userdataindex on userdata (id, userId)",
                                "create table if not exists schema_version (table_name primary key, version)",
                                //pragmas
                                "pragma temp_store = memory"
                               };

            RunQueries(queries);
        }

        /// <summary>
        /// Saves the user data.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="userDataId">The user data id.</param>
        /// <param name="userData">The user data.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// userData
        /// or
        /// cancellationToken
        /// or
        /// userId
        /// or
        /// userDataId
        /// </exception>
        public Task SaveUserData(Guid userId, Guid userDataId, UserItemData userData, CancellationToken cancellationToken)
        {
            if (userData == null)
            {
                throw new ArgumentNullException("userData");
            }
            if (cancellationToken == null)
            {
                throw new ArgumentNullException("cancellationToken");
            }
            if (userId == Guid.Empty)
            {
                throw new ArgumentNullException("userId");
            }
            if (userDataId == Guid.Empty)
            {
                throw new ArgumentNullException("userDataId");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return Task.Run(() =>
            {
                var serialized = _protobufSerializer.SerializeToBytes(userData);

                cancellationToken.ThrowIfCancellationRequested();

                var cmd = connection.CreateCommand();
                cmd.CommandText = "replace into userdata (id, userId, data) values (@1, @2, @3)";
                cmd.AddParam("@1", userDataId);
                cmd.AddParam("@2", userId);
                cmd.AddParam("@3", serialized);
                QueueCommand(cmd);
            });
        }

        /// <summary>
        /// Gets the user data.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="userDataId">The user data id.</param>
        /// <returns>Task{UserItemData}.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// userId
        /// or
        /// userDataId
        /// </exception>
        public async Task<UserItemData> GetUserData(Guid userId, Guid userDataId)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentNullException("userId");
            }
            if (userDataId == Guid.Empty)
            {
                throw new ArgumentNullException("userDataId");
            }

            var cmd = connection.CreateCommand();
            cmd.CommandText = "select data from userdata where id = @id and userId=@userId";

            var idParam = cmd.Parameters.Add("@id", DbType.Guid);
            idParam.Value = userDataId;

            var userIdParam = cmd.Parameters.Add("@userId", DbType.Guid);
            userIdParam.Value = userId;

            using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult | CommandBehavior.SingleRow).ConfigureAwait(false))
            {
                if (reader.Read())
                {
                    using (var stream = GetStream(reader, 0))
                    {
                        return _protobufSerializer.DeserializeFromStream<UserItemData>(stream);
                    }
                }
            }

            return null;
        }
    }
}
