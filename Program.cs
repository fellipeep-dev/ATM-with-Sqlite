using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public static class App
{
    private const string DbFile = "atm.db";

    public static void Main()
    {
        var db = new Database($"Data Source={DbFile}");
        db.EnsureCreated();

        var repo = new Repository(db);
        var service = new BankingService(repo);

        while (true)
        {
            Console.WriteLine("==== Caixa Eletrônico ====");
            Console.WriteLine("1 - Criar Conta");
            Console.WriteLine("2 - Depositar");
            Console.WriteLine("3 - Sacar");
            Console.WriteLine("4 - Transferir");
            Console.WriteLine("5 - Consultar Saldo");
            Console.WriteLine("6 - Consultar Histórico");
            Console.WriteLine("0 - Sair");
            Console.Write("Escolha uma opção: ");

            var input = Console.ReadLine();
            
            switch (input)
            {
                case "1":
                    CriarConta(service);
                    break;
                case "2":
                    Depositar(service);
                    break;
                case "3":
                    Sacar(service);
                    break;
                case "4":
                    Transferir(service);
                    break;
                case "5":
                    ConsultarSaldo(service);
                    break;
                case "6":
                    ConsultarHistorico(service);
                    break;
                case "0":
                    Console.WriteLine("Até mais!");
                    return;
                default:
                    Console.WriteLine("Opção inválida.");
                    break;
            }
            
        }
    }

    private static void CriarConta(BankingService service)
    {
        var holder = Prompt("Nome do titular: ");

        var account = service.CreateAccount(holder);

        Console.WriteLine($"Conta criada! Número: {account.Number} | Titular: {account.HolderName} | Saldo: {account.Balance:C}");
    }

    private static void Depositar(BankingService service)
    {
        var acc = Prompt("Número da conta: ");

        var amount = PromptDecimal("Valor do depósito (maior que 0): ");

        var newBalance = service.Deposit(acc, amount);

        Console.WriteLine($"Depósito realizado. Saldo atual: {newBalance:C}");
    }

    private static void Sacar(BankingService service)
    {
        var acc = Prompt("Número da conta: ");

        var amount = PromptDecimal("Valor do saque (maior que 0): ");

        var newBalance = service.Withdraw(acc, amount);

        Console.WriteLine($"Saque realizado. Saldo atual: {newBalance:C}");
    }

    private static void Transferir(BankingService service)
    {
        var src = Prompt("Conta de origem: ");

        var dst = Prompt("Conta de destino: ");

        var amount = PromptDecimal("Valor da transferência (maior que 0): ");

        service.Transfer(src, dst, amount);

        Console.WriteLine("Transferência realizada com sucesso!");
    }

    private static void ConsultarSaldo(BankingService service)
    {
        var acc = Prompt("Número da conta: ");

        var balance = service.GetBalance(acc);

        Console.WriteLine($"Saldo da conta {acc}: {balance:C}");
    }

    private static void ConsultarHistorico(BankingService service)
    {
        var acc = Prompt("Número da conta: ");

        var history = service.GetHistory(acc);

        Console.WriteLine($"Histórico de transações da conta {acc}:");

        foreach (var t in history)
        {
            var direction = t.Type == TransactionType.Transfer && t.SourceAccountNumber == acc ? $"→ {t.DestinationAccountNumber} " :
                            t.Type == TransactionType.Transfer && t.DestinationAccountNumber == acc ? $"← {t.SourceAccountNumber} " : "";

            Console.WriteLine($"[{t.Timestamp:yyyy-MM-dd HH:mm:ss}] {t.Type} {direction}| Valor: {t.Amount:C}");
        }
    }

    private static string Prompt(string label)
    {
        Console.Write(label);

        return (Console.ReadLine() ?? string.Empty).Trim();
    }

    private static decimal PromptDecimal(string label)
    {
        while (true)
        {
            Console.Write(label);

            var input = (Console.ReadLine() ?? string.Empty).Trim();

            if (decimal.TryParse(input, out var value))
            {
                return value;
            }

            Console.WriteLine("Valor inválido. Tente novamente.");
        }
    }
}

// ----------------------- Domain ----------------------------- \\

public enum TransactionType { Deposit = 1, Withdrawal = 2, Transfer = 3 }

public sealed class Account
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public sealed class TransactionRecord
{
    public long Id { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
    public string? SourceAccountNumber { get; set; }
    public string? DestinationAccountNumber { get; set; }
}

// ----------------------- Domain ----------------------------- \\


// ---------------------- Infrastructure ---------------------- \\

public sealed class Database
{
    private readonly string _connectionString;

    public Database(string connectionString) => _connectionString = connectionString;

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        return conn;
    }

    public void EnsureCreated()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Accounts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT NOT NULL UNIQUE,
            HolderName TEXT NOT NULL,
            Balance REAL NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS Transactions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Type INTEGER NOT NULL CHECK(Type IN (1,2,3)),
            Amount REAL NOT NULL,
            Timestamp TEXT NOT NULL,
            SourceAccountNumber TEXT,
            DestinationAccountNumber TEXT,
            FOREIGN KEY (SourceAccountNumber) REFERENCES Accounts(Number) ON UPDATE CASCADE,
            FOREIGN KEY (DestinationAccountNumber) REFERENCES Accounts(Number) ON UPDATE CASCADE
        );";
        cmd.ExecuteNonQuery();
    }
}

// ---------------------- Infrastructure ---------------------- \\


// ---------------------- Repository -------------------------- \\

public sealed class Repository
{
    private readonly Database _db;

    public Repository(Database db) => _db = db;
    public Database Db => _db;

    public Account CreateAccount(string holderName)
    {
        if (string.IsNullOrWhiteSpace(holderName))
            throw new Exception("Nome do titular é obrigatório.");

        var accountNumber = GenerateUniqueNumber();

        using var conn = _db.OpenConnection();

        using var cmd = conn.CreateCommand();

        cmd.CommandText = "INSERT INTO Accounts (Number, HolderName, Balance) VALUES ($n, $h, $b); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", accountNumber);
        cmd.Parameters.AddWithValue("$h", holderName);
        cmd.Parameters.AddWithValue("$b", 0.0);
        cmd.ExecuteScalar();

        return new Account { Number = accountNumber, HolderName = holderName, Balance = 0m };
    }

    public Account GetAccountByNumber(string number, SqliteConnection? externalConn = null, SqliteTransaction? tx = null)
    {
        var conn = externalConn ?? _db.OpenConnection();

        try
        {
            using var cmd = conn.CreateCommand();

            if (tx != null) cmd.Transaction = tx;

            cmd.CommandText = "SELECT Id, Number, HolderName, Balance FROM Accounts WHERE Number = $n";
            cmd.Parameters.AddWithValue("$n", number);

            using var r = cmd.ExecuteReader();

            if (!r.Read()) throw new Exception("Conta não encontrada.");

            return new Account
            {
                Id = r.GetInt64(0),
                Number = r.GetString(1),
                HolderName = r.GetString(2),
                Balance = Convert.ToDecimal(r.GetDouble(3)),
            };
        }
        finally
        {
            if (externalConn == null) conn.Dispose();
        }
    }

    public void UpdateBalance(string number, decimal newBalance, SqliteConnection? externalConn = null, SqliteTransaction? tx = null)
    {
        var conn = externalConn ?? _db.OpenConnection();

        try
        {
            using var cmd = conn.CreateCommand();

            if (tx != null) cmd.Transaction = tx;

            cmd.CommandText = "UPDATE Accounts SET Balance = $b WHERE Number = $n";
            cmd.Parameters.AddWithValue("$b", Convert.ToDouble(newBalance));
            cmd.Parameters.AddWithValue("$n", number);

            if (cmd.ExecuteNonQuery() == 0) throw new Exception("Conta não encontrada para atualização.");
        }
        finally
        {
            if (externalConn == null) conn.Dispose();
        }
    }

    public void InsertTransaction(TransactionRecord t, SqliteConnection? externalConn = null, SqliteTransaction? tx = null)
    {
        var conn = externalConn ?? _db.OpenConnection();

        try
        {
            using var cmd = conn.CreateCommand();

            if (tx != null) cmd.Transaction = tx;

            cmd.CommandText = @"INSERT INTO Transactions (Type, Amount, Timestamp, SourceAccountNumber, DestinationAccountNumber)
                               VALUES ($type, $amount, $ts, $src, $dst);";
            cmd.Parameters.AddWithValue("$type", (int)t.Type);
            cmd.Parameters.AddWithValue("$amount", Convert.ToDouble(t.Amount));
            cmd.Parameters.AddWithValue("$ts", t.Timestamp.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$src", (object?)t.SourceAccountNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dst", (object?)t.DestinationAccountNumber ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (externalConn == null) conn.Dispose();
        }
    }

    public decimal GetBalance(string number)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT Balance FROM Accounts WHERE Number = $n";
        cmd.Parameters.AddWithValue("$n", number);

        var result = cmd.ExecuteScalar();

        if (result is null) throw new Exception("Conta não encontrada.");

        return Convert.ToDecimal(result);
    }

    public TransactionRecord[] GetHistory(string number)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"SELECT Id, Type, Amount, Timestamp, SourceAccountNumber, DestinationAccountNumber
                             FROM Transactions
                             WHERE SourceAccountNumber = $n OR DestinationAccountNumber = $n
                             ORDER BY datetime(Timestamp) DESC";
        cmd.Parameters.AddWithValue("$n", number);

        using var r = cmd.ExecuteReader();

        var list = new List<TransactionRecord>();

        while (r.Read())
        {
            list.Add(new TransactionRecord
            {
                Id = r.GetInt64(0),
                Type = (TransactionType)r.GetInt32(1),
                Amount = Convert.ToDecimal(r.GetDouble(2)),
                Timestamp = DateTime.Parse(r.GetString(3)),
                SourceAccountNumber = r.IsDBNull(4) ? null : r.GetString(4),
                DestinationAccountNumber = r.IsDBNull(5) ? null : r.GetString(5)
            });
        }

        return list.ToArray();
    }

    private string GenerateUniqueNumber()
    {
        var rnd = new Random();

        using var conn = _db.OpenConnection();

        for (int i = 0; i < 20; i++)
        {
            var candidate = rnd.Next(1000, 9999).ToString();

            using var check = conn.CreateCommand();

            check.CommandText = "SELECT COUNT(1) FROM Accounts WHERE Number = $n";
            check.Parameters.AddWithValue("$n", candidate);

            var exists = Convert.ToInt32(check.ExecuteScalar() ?? 0) > 0;

            if (!exists) return candidate;
        }

        throw new Exception("Não foi possível gerar número de conta único. Tente novamente.");
    }
}

// ---------------------- Repository -------------------------- \\


// ---------------------- Use-case ------------------------- \\

public sealed class BankingService
{
    private readonly Repository _repo;
    public BankingService(Repository repo) => _repo = repo;

    public Account CreateAccount(string holderName)
    {
        return _repo.CreateAccount(holderName);
    }

    public decimal Deposit(string accountNumber, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(accountNumber)) throw new Exception("Número da conta é obrigatório.");

        if (amount <= 0) throw new Exception("Valor deve ser positivo.");

        var db = _repo.Db;
        using var sqliteConn = db.OpenConnection();
        using var tx = sqliteConn.BeginTransaction(IsolationLevel.Serializable);

        var acc = _repo.GetAccountByNumber(accountNumber, sqliteConn, tx);

        var newBalance = acc.Balance + amount;

        _repo.UpdateBalance(accountNumber, newBalance, sqliteConn, tx);

        _repo.InsertTransaction(new TransactionRecord
        {
            Type = TransactionType.Deposit,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            SourceAccountNumber = accountNumber,
            DestinationAccountNumber = null
        }, sqliteConn, tx);

        tx.Commit();

        return newBalance;
    }

    public decimal Withdraw(string accountNumber, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(accountNumber)) throw new Exception("Número da conta é obrigatório.");

        if (amount <= 0) throw new Exception("Valor deve ser positivo.");

        var db = _repo.Db;
        using var sqliteConn = db.OpenConnection();
        using var tx = sqliteConn.BeginTransaction(IsolationLevel.Serializable);

        var acc = _repo.GetAccountByNumber(accountNumber, sqliteConn, tx);

        if (acc.Balance < amount) throw new Exception("Saldo insuficiente.");

        var newBalance = acc.Balance - amount;

        _repo.UpdateBalance(accountNumber, newBalance, sqliteConn, tx);
        _repo.InsertTransaction(new TransactionRecord
        {
            Type = TransactionType.Withdrawal,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            SourceAccountNumber = accountNumber,
            DestinationAccountNumber = null
        }, sqliteConn, tx);

        tx.Commit();

        return newBalance;
    }

    public void Transfer(string sourceNumber, string destNumber, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(sourceNumber) || string.IsNullOrWhiteSpace(destNumber))
            throw new Exception("Contas de origem e destino são obrigatórias.");

        if (sourceNumber == destNumber)
            throw new Exception("Não é possível transferir para a mesma conta.");

        if (amount <= 0) throw new Exception("Valor deve ser positivo.");

        var db = _repo.Db;
        using var conn = db.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

        var src = _repo.GetAccountByNumber(sourceNumber, conn, tx);
        var dst = _repo.GetAccountByNumber(destNumber, conn, tx);

        if (src.Balance < amount) throw new Exception("Saldo insuficiente na conta de origem.");

        _repo.UpdateBalance(sourceNumber, src.Balance - amount, conn, tx);
        _repo.UpdateBalance(destNumber, dst.Balance + amount, conn, tx);

        _repo.InsertTransaction(new TransactionRecord
        {
            Type = TransactionType.Transfer,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            SourceAccountNumber = sourceNumber,
            DestinationAccountNumber = destNumber
        }, conn, tx);

        tx.Commit();
    }

    public decimal GetBalance(string accountNumber)
    {
        return _repo.GetBalance(accountNumber);
    }

    public TransactionRecord[] GetHistory(string accountNumber)
    {
        return _repo.GetHistory(accountNumber);
    }
}

// ---------------------- Use-case ------------------------- \\
