using System;
using System.Text;
using System.Data;
using System.Data.SQLite;
using PinTransferWPF;
using System.Data.Common;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Documents;
using System.Runtime.CompilerServices;
using System.Linq;
using System.CodeDom.Compiler;
using System.ComponentModel.Design;

namespace Integration
{
    public class LabwareManager
    {
        private readonly SQLiteConnection _connection;
        private readonly bool _shouldDisposeConnection;
        private string _connectionString;
        public LabwareManager(string connectionString)
        {
            _connectionString = connectionString;
            EnsureLabwareTableExists();
        }

        private void EnsureLabwareTableExists()
        {
            using (var _connection = new SQLiteConnection(_connectionString))
            {
                _connection.Open();
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS Labware (
                                                Identifier TEXT NOT NULL PRIMARY KEY,
                                                Height REAL NOT NULL,
                                                NestedHeight REAL NOT NULL,
                                                IsLowVolume INTEGER NOT NULL,
                                                OffsetY REAL NOT NULL,
                                                Type TEXT NOT NULL
                                                )", _connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public void AddLabware(Labware labware)
        {
            using (var _connection = new SQLiteConnection(_connectionString))
            {
                _connection.Open();
                // Check if a labware with the same Identifier already exists
                using (var command = new SQLiteCommand("SELECT COUNT(*) FROM Labware WHERE Identifier = @Identifier", _connection))
                {
                    command.Parameters.AddWithValue("@Identifier", labware.Identifier);
                    int count = Convert.ToInt32(command.ExecuteScalar());
                    if (count > 0)
                    {
                        throw new Exception($"A plate with the identifier \"'{labware.Identifier}'\" already exists.");
                    }
                }

                // Add the new labware
                using (var command = new SQLiteCommand("INSERT INTO Labware (Identifier, Height, NestedHeight, IsLowVolume, OffsetY, Type)" +
                    " VALUES (@Identifier, @Height, @NestedHeight, @IsLowVolume, @OffsetY, @Type)", _connection))
                {
                    command.Parameters.AddWithValue("@Identifier", labware.Identifier);
                    command.Parameters.AddWithValue("@Height", labware.Height);
                    command.Parameters.AddWithValue("@NestedHeight", labware.NestedHeight);
                    command.Parameters.AddWithValue("@IsLowVolume", labware.IsLowVolume);
                    command.Parameters.AddWithValue("@OffsetY", labware.OffsetY);
                    command.Parameters.AddWithValue("@Type", labware.Type);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void UpdateLabware(Labware labware, string newIdentifier = null)
        {
            if (newIdentifier is null)
            {
                newIdentifier = labware.Identifier;
            }
            using (var _connection = new SQLiteConnection(_connectionString))
            {
                _connection.Open();

                // Get the existing labware with the same identifier
                using (var command = new SQLiteCommand("SELECT * FROM Labware WHERE Identifier = @Identifier", _connection))
                {
                    command.Parameters.AddWithValue("@Identifier", labware.Identifier);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            throw new Exception($"Labware with identifier '{labware.Identifier}' not found.");
                        }

                        // If the identifier is different, check if the new identifier already exists
                        if (newIdentifier != labware.Identifier)
                        {
                            using (var checkCommand = new SQLiteCommand("SELECT COUNT(*) FROM Labware WHERE Identifier = @Identifier", _connection))
                            {
                                checkCommand.Parameters.AddWithValue("@Identifier", newIdentifier);
                                int count = Convert.ToInt32(checkCommand.ExecuteScalar());
                                if (count > 0)
                                {
                                    throw new Exception($"A labware with the identifier \"" + newIdentifier + "\" already exists.");
                                }
                            }
                        }

                        // Update the labware
                        using (var updateCommand = new SQLiteCommand("UPDATE Labware SET Identifier = @NewIdentifier, Height = @Height, NestedHeight = @NestedHeight, IsLowVolume = @IsLowVolume, OffsetY = @OffsetY, Type = @Type WHERE Identifier = @Identifier", _connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Identifier", labware.Identifier);
                            updateCommand.Parameters.AddWithValue("@Height", labware.Height);
                            updateCommand.Parameters.AddWithValue("@NestedHeight", labware.NestedHeight);
                            updateCommand.Parameters.AddWithValue("@IsLowVolume", labware.IsLowVolume);
                            updateCommand.Parameters.AddWithValue("@OffsetY", labware.OffsetY);
                            updateCommand.Parameters.AddWithValue("@Type", labware.Type);
                            updateCommand.Parameters.AddWithValue("@NewIdentifier", newIdentifier);
                            updateCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public void DeleteLabware(Labware labware)
        {
            using (var _connection = new SQLiteConnection(_connectionString))
            {
                _connection.Open();

                using (var checkCommand = new SQLiteCommand("SELECT COUNT(*) FROM Labware WHERE Identifier = @Identifier", _connection))
                {
                    checkCommand.Parameters.AddWithValue("@Identifier", labware.Identifier);
                    int count = Convert.ToInt32(checkCommand.ExecuteScalar());
                    if (count == 0)
                    {
                        throw new Exception($"No labware with the identifier '{labware.Identifier}' exists.");
                    }
                }

                using (var command = new SQLiteCommand("DELETE FROM Labware WHERE Identifier = @Identifier", _connection))
                {
                    command.Parameters.AddWithValue("@Identifier", labware.Identifier);
                    command.ExecuteNonQuery();
                }
            }
        }

        public List<Labware> GetAllLabware()
        {
            var labware = new List<Labware>();
            using (var _connection = new SQLiteConnection(_connectionString))
            {
                _connection.Open();
                using (var command = new SQLiteCommand("SELECT * FROM Labware", _connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var Identifier = reader.GetString(0);
                            var Height = reader.GetDouble(1);
                            var NestedHeight = reader.GetDouble(2);
                            var IsLowVolume = reader.GetInt16(3);
                            var OffsetY = reader.GetDouble(4);
                            var Type = reader.GetString(5);
                            labware.Add(new Labware { Identifier = Identifier, Height = Height, NestedHeight = NestedHeight,
                                IsLowVolume = IsLowVolume, OffsetY = OffsetY, Type = Type});
                        }
                    }
                }
            }
            return labware;
        }
    }

    public class Labware
    {
        public string Identifier { get; set; }
        public double Height { get; set; }
        public double NestedHeight { get; set; }
        public int IsLowVolume { get; set; }
        public double OffsetY { get; set; }
        public string Type { get; set; }
        public bool Lidded { get; set; }
    }
    public class RunInfo
    {
        public string RunID { get; set; }
        public DateTime TimeRun { get; set; }
        public int? ScreenNumber { get; set; }
        public string UserName { get; set; }
        public int CurrentJournalLine { get; set; }
        public string JournalID { get; set; }
        public RunInfo()
        {
            ScreenNumber = null;
        }
    }

    public class RunState
    {
        public string JournalID { get; set; }
        public int EpsonCommandID { get; set; }
        public int KX2CommandID { get; set; }
        public string SerializedToolStates { get; set; }
        public string SerializedArmStates { get; set; }
        public string SerializedCarouselStates { get; set; }
        public DateTime LastUpdated { get; set; }
        public int Completed { get; set; }

        public RunState()
        {
            Completed = 0;
            LastUpdated = DateTime.Now;
        }
    }

    public class RunLogger
    {
        private readonly string _connectionString;

        public RunLogger(string connectionString)
        {
            _connectionString = connectionString;
            CreateRunStateTable();
        }

        // Helper method to check if a table exists
        private bool TableExists(SQLiteConnection connection, string tableName)
        {
            using (var command = new SQLiteCommand($"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';", connection))
            {
                using (var reader = command.ExecuteReader())
                {
                    return reader.HasRows;
                }
            }
        }

        public void MarkRunAsCompleted(string journalID)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand("UPDATE RunState SET Completed = 1 WHERE JournalID = @JournalID", connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalID);
                    command.ExecuteNonQuery();
                }
            }
        }

        public RunState LoadMostRecentUnfinishedRunState()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                if (!TableExists(connection, "RunState"))
                {
                    return null;
                }

                using (var command = new SQLiteCommand("SELECT * FROM RunState WHERE Completed = 0 ORDER BY LastUpdated DESC LIMIT 1", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new RunState
                            {
                                JournalID = reader.GetString(0),
                                EpsonCommandID = reader.GetInt32(1),
                                KX2CommandID = reader.GetInt32(2),
                                SerializedToolStates = reader.GetString(3),
                                SerializedArmStates = reader.GetString(4),
                                SerializedCarouselStates = reader.GetString(5),
                                LastUpdated = DateTime.Parse(reader.GetString(6)),
                                Completed = reader.GetInt32(7)
                            };
                        }
                    }
                }
            }
            return null;
        }

        public RunState GetCurrentRunState(string journalID)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand("SELECT * FROM RunState WHERE JournalID = @JournalID", connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalID);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new RunState
                            {
                                JournalID = reader["JournalID"].ToString(),
                                EpsonCommandID = Convert.ToInt32(reader["EpsonCommandID"]),
                                KX2CommandID = Convert.ToInt32(reader["KX2CommandID"]),
                                SerializedToolStates = reader["SerializedToolStates"].ToString(),
                                SerializedArmStates = reader["SerializedArmStates"].ToString(),
                                SerializedCarouselStates = reader["SerializedCarouselStates"].ToString(),
                                LastUpdated = DateTime.Parse(reader["LastUpdated"].ToString()),
                                Completed = Convert.ToInt32(reader["Completed"]),
                            };
                        }
                        // If no existing state is found, return a new RunState with default values
                        return new RunState
                        {
                            JournalID = journalID,
                            EpsonCommandID = 1,
                            KX2CommandID = 1,
                            SerializedToolStates = "",
                            SerializedArmStates = "",
                            SerializedCarouselStates = "",
                            LastUpdated = DateTime.UtcNow,
                            Completed = 0
                        };
                    }
                }
            }
        }
        
        public void CreateRun(RunInfo runInfo)
        {
            // Create a new SQLite connection
            using (var _connection = new SQLiteConnection("Data Source=" + Parameters.LoggingDatabase))
            {
                _connection.Open();
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS RunInfo (
                                                RunID INTEGER PRIMARY KEY AUTOINCREMENT,
                                                JournalID TEXT NOT NULL,
                                                TimeRun TEXT NOT NULL,
                                                ScreenNumber INTEGER NOT NULL,
                                                UserName TEXT NOT NULL,
                                                FOREIGN KEY (JournalID) REFERENCES Journals(JournalID)
                                                )", _connection))
                {
                    command.ExecuteNonQuery();
                }

                // Add the runinfo
                using (var command = new SQLiteCommand("INSERT INTO RunInfo (RunID, TimeRun, ScreenNumber, UserName, JournalID)" +
                    " VALUES (@RunID, @TimeRun, @ScreenNumber, @UserName, @JournalID)", _connection))
                {
                    command.Parameters.AddWithValue("@RunID", null);
                    command.Parameters.AddWithValue("@JournalID", runInfo.JournalID);
                    command.Parameters.AddWithValue("@TimeRun", runInfo.TimeRun.ToString());
                    command.Parameters.AddWithValue("@ScreenNumber", runInfo.ScreenNumber);
                    command.Parameters.AddWithValue("@UserName", runInfo.UserName);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void CreateRunStateTable()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS RunState (
                                                JournalID TEXT PRIMARY KEY,
                                                EpsonCommandID INTEGER NOT NULL,
                                                KX2CommandID INTEGER NOT NULL,
                                                SerializedToolStates TEXT NOT NULL,
                                                SerializedArmStates TEXT NOT NULL,
                                                SerializedCarouselStates TEXT NOT NULL,
                                                LastUpdated TEXT NOT NULL,
                                                Completed INTEGER NOT NULL)", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SaveRunState(RunState state)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS RunState (
                                                JournalID TEXT PRIMARY KEY,
                                                EpsonCommandID INTEGER NOT NULL,
                                                KX2CommandID INTEGER NOT NULL,
                                                SerializedToolStates TEXT NOT NULL,
                                                SerializedArmStates TEXT NOT NULL,
                                                SerializedCarouselStates TEXT NOT NULL,
                                                LastUpdated TEXT NOT NULL,
                                                Completed INTEGER NOT NULL)", connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(@"INSERT OR REPLACE INTO RunState 
                (JournalID, EpsonCommandID, KX2CommandID, SerializedToolStates, SerializedArmStates, SerializedCarouselStates, LastUpdated, Completed)
                VALUES (@JournalID, @EpsonCommandID, @KX2CommandID, @SerializedToolStates, @SerializedArmStates, @SerializedCarouselStates, @LastUpdated, @Completed)", connection))
                {
                    command.Parameters.AddWithValue("@JournalID", state.JournalID);
                    command.Parameters.AddWithValue("@EpsonCommandID", state.EpsonCommandID);
                    command.Parameters.AddWithValue("@KX2CommandID", state.KX2CommandID);
                    command.Parameters.AddWithValue("@SerializedToolStates", state.SerializedToolStates);
                    command.Parameters.AddWithValue("@SerializedArmStates", state.SerializedArmStates);
                    command.Parameters.AddWithValue("@SerializedCarouselStates", state.SerializedCarouselStates);
                    command.Parameters.AddWithValue("@LastUpdated", state.LastUpdated.ToString("O"));
                    command.Parameters.AddWithValue("@Completed", state.Completed);
                    command.ExecuteNonQuery();
                }
            }
        }

        public RunState LoadRunState(string journalID)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS RunState (
                                                JournalID TEXT PRIMARY KEY,
                                                EpsonCommandID INTEGER NOT NULL,
                                                KX2CommandID INTEGER NOT NULL,
                                                SerializedToolStates TEXT NOT NULL,
                                                SerializedArmStates TEXT NOT NULL,
                                                SerializedCarouselStates TEXT NOT NULL,
                                                LastUpdated TEXT NOT NULL,
                                                Completed INTEGER NOT NULL)", connection))
                {
                    command.ExecuteNonQuery();
                }
                using (var command = new SQLiteCommand("SELECT * FROM RunState WHERE JournalID = @JournalID", connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalID);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new RunState
                            {
                                JournalID = reader.GetString(0),
                                EpsonCommandID = reader.GetInt32(1),
                                KX2CommandID = reader.GetInt32(2),
                                SerializedToolStates = reader.GetString(3),
                                SerializedArmStates = reader.GetString(4),
                                SerializedCarouselStates = reader.GetString(5),
                                LastUpdated = DateTime.Parse(reader.GetString(6)),
                                Completed = reader.GetInt32(1),
                            };
                        }
                    }
                }
            }
            return null;
        }

        public class JournalInfo
        {
            public string JournalID { get; set;}
            public List<Plate> SourcePlates { get; set; }
            public List<Plate> DestinationPlates { get; set; }
        }
        public void CreateJournal(JournalInfo journalInfo)
        {
            // Create a new SQLite connection
            using (var _connection = new SQLiteConnection("Data Source=" + Parameters.LoggingDatabase))
            {
                _connection.Open();
                // Create the Journals tables
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS Journals (
                                                        JournalID TEXT PRIMARY KEY
                                                        )", _connection))
                {
                    command.ExecuteNonQuery();
                }
                using (var command = new SQLiteCommand("INSERT INTO Journals (JournalID)" +
                    " VALUES (@JournalID)", _connection))
                {
                    command.Parameters.AddWithValue("@JournalID", journalInfo.JournalID);
                    command.ExecuteNonQuery();
                }
                // Create the InstrumentCommands tables
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS InstrumentCommands (
                                                        CommandID INTEGER NOT NULL,
                                                        Instrument TEXT NOT NULL,
                                                        JournalID TEXT NOT NULL,
                                                        Command TEXT NOT NULL,
                                                        PRIMARY KEY (CommandID, Instrument, JournalID)
                                                        FOREIGN KEY (JournalID) REFERENCES Journals(JournalID)
                                                        )", _connection))
                {
                    command.ExecuteNonQuery();
                }

                // Insert data into the Journal table
                InsertData(_connection, journalInfo);
            }
        }
        private void InsertData(SQLiteConnection connection, JournalInfo journalInfo)
        {
            var sb = new StringBuilder();
            var kx2Commands = new List<string>();
            var epsonCommands = new List<string>();
            int transferVolume = 0;
            int previousTransferVolume = 0;
            int toolVolumeAttached = 0;
            bool toolAttached = false;

            foreach (SourcePlate plate in journalInfo.SourcePlates)
            {
                kx2Commands.Add("Get Source from Stack");
                kx2Commands.Add("Set Source to Stage");
                transferVolume = plate.replicates.Item1;
                if (previousTransferVolume != transferVolume)
                {
                    if (toolAttached)
                    {
                        epsonCommands.Add("Detach " + toolVolumeAttached);
                        toolAttached = false;
                    }
                    epsonCommands.Add("Attach " + transferVolume.ToString());
                    toolAttached = true;
                    toolVolumeAttached = transferVolume;
                    previousTransferVolume = transferVolume;
                }

                for (int replicate = 0; replicate < plate.replicates.Item2; replicate += 1)
                {
                    kx2Commands.Add("Get Destination from Stack");
                    kx2Commands.Add("Set Destination to Stage");
                    kx2Commands.Add("Move Safe");
                    kx2Commands.Add("Get Destination from Stage");
                    kx2Commands.Add("Set Destination to Stack");
                    epsonCommands.Add("Wash " + transferVolume.ToString());
                    epsonCommands.Add("Transfer " + transferVolume.ToString());
                    epsonCommands.Add("Move Safe " + transferVolume.ToString());
                }

                kx2Commands.Add("Get Source from Stage");
                kx2Commands.Add("Set Source to Stack");
            }
            epsonCommands.Add("Wash " + transferVolume.ToString());
            epsonCommands.Add("Detach " + toolVolumeAttached);

            for (int i = 0; i < kx2Commands.Count; i++)
            {
                sb.Append($"INSERT INTO InstrumentCommands (JournalID, Command, Instrument, CommandID) VALUES ('{journalInfo.JournalID}', '{kx2Commands[i]}', 'KX2', {i + 1});");
            }

            for (int i = 0; i < epsonCommands.Count; i++)
            {
                sb.Append($"INSERT INTO InstrumentCommands (JournalID, Command, Instrument, CommandID) VALUES ('{journalInfo.JournalID}', '{epsonCommands[i]}', 'Epson', {i + 1});");
            }

            using (var command = new SQLiteCommand(sb.ToString(), connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }

    public class TransferJournalManager
    {
        

        public TransferJournalManager()
        {
        }
    }
}
