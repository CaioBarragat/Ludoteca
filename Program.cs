// Program.cs
// Requer: .NET 9
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludoteca
{
    // Classe Jogo
    public class Jogo
    {
        private static int _nextId = 1;
        public int Id { get; private set; }
        private string _nome;
        public string Nome
        {
            get => _nome;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Nome do jogo não pode ser vazio.");
                _nome = value.Trim();
            }
        }
        public string Categoria { get; private set; }
        public bool EstaEmprestado { get; private set; }

        public Jogo(string nome, string categoria)
        {
            Id = _nextId++;
            Nome = nome;
            Categoria = string.IsNullOrWhiteSpace(categoria) ? "Outro" : categoria.Trim();
            EstaEmprestado = false;
        }

        public void MarcarEmprestado() => EstaEmprestado = true;
        public void MarcarDisponivel() => EstaEmprestado = false;
    }

    // Classe Membro
    public class Membro
    {
        private static int _nextId = 1;
        public int Id { get; private set; }
        private string _nome;
        public string Nome
        {
            get => _nome;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Nome do membro não pode ser vazio.");
                _nome = value.Trim();
            }
        }
        public string Contato { get; private set; }

        public Membro(string nome, string contato)
        {
            Id = _nextId++;
            Nome = nome;
            Contato = string.IsNullOrWhiteSpace(contato) ? "-" : contato.Trim();
        }
    }

    // Classe Emprestimo
    public class Emprestimo
    {
        private static int _nextId = 1;
        public int Id { get; private set; }
        public int JogoId { get; private set; }
        public int MembroId { get; private set; }
        public DateTime DataEmprestimo { get; private set; }
        public DateTime DataPrevistaDevolucao { get; private set; }
        public DateTime? DataDevolucao { get; private set; }
        public decimal MultaPago { get; private set; }

        // taxa diária de multa (pode ser alterada)
        [JsonIgnore]
        public static decimal MultaDiaria = 2.00m;

        public Emprestimo(int jogoId, int membroId, DateTime dataEmprestimo, int diasPraDevolucao = 7)
        {
            Id = _nextId++;
            JogoId = jogoId;
            MembroId = membroId;
            DataEmprestimo = dataEmprestimo;
            DataPrevistaDevolucao = dataEmprestimo.AddDays(diasPraDevolucao);
            DataDevolucao = null;
            MultaPago = 0m;
        }

        public bool EstaDevolvido => DataDevolucao.HasValue;

        public int DiasAtraso()
        {
            if (!DataDevolucao.HasValue) return 0;
            var atraso = (DataDevolucao.Value.Date - DataPrevistaDevolucao.Date).Days;
            return atraso > 0 ? atraso : 0;
        }

        public decimal CalcularMulta()
        {
            int dias = DiasAtraso();
            return dias * MultaDiaria;
        }

        public void RegistrarDevolucao(DateTime dataDevolucao, decimal multaPaga)
        {
            DataDevolucao = dataDevolucao;
            MultaPago = multaPaga;
        }
    }

    // BibliotecaJogos: gerencia tudo
    public class BibliotecaJogos
    {
        public List<Jogo> Jogos { get; private set; } = new();
        public List<Membro> Membros { get; private set; } = new();
        public List<Emprestimo> Emprestimos { get; private set; } = new();

        private readonly string _dataDir = "data";
        private readonly string _arquivoJson;
        private readonly string _relatorioFile;
        private readonly string _logFile;

        public BibliotecaJogos()
        {
            _arquivoJson = Path.Combine(_dataDir, "biblioteca.json");
            _relatorioFile = Path.Combine(_dataDir, "relatorio.txt");
            _logFile = Path.Combine(_dataDir, "debug.log");
            Directory.CreateDirectory(_dataDir);
        }

        // [AV1-3] Métodos Salvar() e Carregar() usando System.Text.Json.
        public void Salvar()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                // salvar contadores de id como parte do estado para manter sequência
                var estado = new
                {
                    Jogos,
                    Membros,
                    Emprestimos,
                    // salvar valores internos de _nextId através de introspecção simples (não ideal em produção)
                    NextIds = new
                    {
                        JogoNext = GetPrivateStaticFieldValue<int>(typeof(Jogo), "_nextId"),
                        MembroNext = GetPrivateStaticFieldValue<int>(typeof(Membro), "_nextId"),
                        EmprestimoNext = GetPrivateStaticFieldValue<int>(typeof(Emprestimo), "_nextId")
                    }
                };
                var json = JsonSerializer.Serialize(estado, options);
                File.WriteAllText(_arquivoJson, json);
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        public void Carregar()
        {
            try
            {
                if (!File.Exists(_arquivoJson)) return;
                var json = File.ReadAllText(_arquivoJson);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var jArray = root.GetProperty("Jogos");
                Jogos = JsonSerializer.Deserialize<List<Jogo>>(jArray.GetRawText()) ?? new();
                var mArray = root.GetProperty("Membros");
                Membros = JsonSerializer.Deserialize<List<Membro>>(mArray.GetRawText()) ?? new();
                var eArray = root.GetProperty("Emprestimos");
                Emprestimos = JsonSerializer.Deserialize<List<Emprestimo>>(eArray.GetRawText()) ?? new();

                // restaurar _nextId
                var nextIds = root.GetProperty("NextIds");
                SetPrivateStaticFieldValue(typeof(Jogo), "_nextId", nextIds.GetProperty("JogoNext").GetInt32());
                SetPrivateStaticFieldValue(typeof(Membro), "_nextId", nextIds.GetProperty("MembroNext").GetInt32());
                SetPrivateStaticFieldValue(typeof(Emprestimo), "_nextId", nextIds.GetProperty("EmprestimoNext").GetInt32());
            }
            catch (Exception ex)
            {
                LogError(ex);
                throw;
            }
        }

        // utilitários para refletir campos privados estáticos (apenas para persistência simples)
        private static T GetPrivateStaticFieldValue<T>(Type t, string fieldName)
        {
            var f = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (T)f.GetValue(null);
        }
        private static void SetPrivateStaticFieldValue(Type t, string fieldName, object value)
        {
            var f = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            f.SetValue(null, value);
        }

        // Cadastrar jogo
        public Jogo CadastrarJogo(string nome, string categoria)
        {
            if (Jogos.Any(j => j.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Já existe um jogo com esse nome.");
            var j = new Jogo(nome, categoria);
            Jogos.Add(j);
            return j;
        }

        // Cadastrar membro
        public Membro CadastrarMembro(string nome, string contato)
        {
            var m = new Membro(nome, contato);
            Membros.Add(m);
            return m;
        }

        // Listar jogos
        public IEnumerable<Jogo> ListarJogos() => Jogos.OrderBy(j => j.Id);

        // Emprestar jogo
        public Emprestimo EmprestarJogo(int jogoId, int membroId, DateTime dataEmprestimo, int diasPraDevolucao = 7)
        {
            var jogo = Jogos.FirstOrDefault(j => j.Id == jogoId) ?? throw new ArgumentException("Jogo não encontrado.");
            var membro = Membros.FirstOrDefault(m => m.Id == membroId) ?? throw new ArgumentException("Membro não encontrado.");
            if (jogo.EstaEmprestado) throw new InvalidOperationException("Jogo já está emprestado."); // [AV1-5]
            var emp = new Emprestimo(jogoId, membroId, dataEmprestimo, diasPraDevolucao);
            Emprestimos.Add(emp);
            jogo.MarcarEmprestado();
            return emp;
        }

        // Devolver jogo
        public (Emprestimo emprestimo, decimal multa) DevolverJogo(int emprestimoId, DateTime dataDevolucao, decimal pagamento)
        {
            var emprestimo = Emprestimos.FirstOrDefault(e => e.Id == emprestimoId) ?? throw new ArgumentException("Empréstimo não encontrado.");
            if (emprestimo.EstaDevolvido) throw new InvalidOperationException("Empréstimo já devolvido.");
            var multa = emprestimo.CalcularMulta();
            if (pagamento < multa) throw new InvalidOperationException($"Pagamento insuficiente. Multa: {multa:C2}");
            emprestimo.RegistrarDevolucao(dataDevolucao, multa);
            var jogo = Jogos.FirstOrDefault(j => j.Id == emprestimo.JogoId);
            jogo?.MarcarDisponivel();
            return (emprestimo, multa);
        }

        // Gerar relatório simples
        public void GerarRelatorio()
        {
            using var sw = new StreamWriter(_relatorioFile, false);
            sw.WriteLine($"Relatório - {DateTime.Now}");
            sw.WriteLine("=== Jogos ===");
            foreach (var j in Jogos)
            {
                sw.WriteLine($"#{j.Id} - {j.Nome} - Categoria: {j.Categoria} - Emprestado: {j.EstaEmprestado}");
            }
            sw.WriteLine();
            sw.WriteLine("=== Empréstimos (ativos) ===");
            var ativos = Emprestimos.Where(e => !e.EstaDevolvido).ToList();
            foreach (var e in ativos)
            {
                var j = Jogos.FirstOrDefault(x => x.Id == e.JogoId);
                var m = Membros.FirstOrDefault(x => x.Id == e.MembroId);
                sw.WriteLine($"Emp#{e.Id} - Jogo: {j?.Nome ?? e.JogoId.ToString()} - Membro: {m?.Nome ?? e.MembroId.ToString()} - Emp.: {e.DataEmprestimo:yyyy-MM-dd} - Prev. Dev.: {e.DataPrevistaDevolucao:yyyy-MM-dd}");
            }
            sw.WriteLine();
            sw.WriteLine("=== Histórico de Devoluções ===");
            var devolvidos = Emprestimos.Where(e => e.EstaDevolvido).ToList();
            foreach (var e in devolvidos)
            {
                var j = Jogos.FirstOrDefault(x => x.Id == e.JogoId);
                var m = Membros.FirstOrDefault(x => x.Id == e.MembroId);
                sw.WriteLine($"Emp#{e.Id} - Jogo: {j?.Nome} - Membro: {m?.Nome} - Dev.: {e.DataDevolucao:yyyy-MM-dd} - Multa: {e.MultaPago:C2}");
            }
        }

        // Log de erros
        public void LogError(Exception ex)
        {
            try
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
                File.AppendAllText(_logFile, msg);
            }
            catch
            {
                // se falhar no log, não queremos lançar nova exceção aqui
            }
        }

        // Exportar relatorioPath getter
        public string RelatorioPath => _relatorioFile;
        public string JsonPath => _arquivoJson;
        public string LogPath => _logFile;
    }

    // Programa principal com menu
    internal class Program
    {
        static void Main(string[] args)
        {
            var biblioteca = new BibliotecaJogos();
            try
            {
                biblioteca.Carregar(); // [AV1-3]
            }
            catch (Exception ex)
            {
                Console.WriteLine("Falha ao carregar dados. Consultar debug.log.");
                biblioteca.LogError(ex);
            }

            bool sair = false;
            while (!sair)
            {
                try
                {
                    Console.WriteLine("=== LUDOTECA .NET ===");
                    Console.WriteLine("1 - Cadastrar jogo");
                    Console.WriteLine("2 - Cadastrar membro");
                    Console.WriteLine("3 - Listar jogos");
                    Console.WriteLine("4 - Emprestar jogo");
                    Console.WriteLine("5 - Devolver jogo");
                    Console.WriteLine("6 - Gerar relatório");
                    Console.WriteLine("7 - Salvar");
                    Console.WriteLine("0 - Sair");
                    Console.Write("Opção: ");
                    var opt = Console.ReadLine();

                    switch (opt)
                    {
                        case "1":
                            // [AV1-4-Cadastrar]
                            CadastrarJogoFlow(biblioteca);
                            break;
                        case "2":
                            // [AV1-4-CadastrarMembro]
                            CadastrarMembroFlow(biblioteca);
                            break;
                        case "3":
                            // [AV1-4-Listar]
                            ListarJogosFlow(biblioteca);
                            break;
                        case "4":
                            // [AV1-4-Emprestar]
                            EmprestarFlow(biblioteca);
                            break;
                        case "5":
                            // [AV1-4-Devolver]
                            DevolverFlow(biblioteca);
                            break;
                        case "6":
                            // [AV1-4-Relatorio]
                            biblioteca.GerarRelatorio();
                            Console.WriteLine($"Relatório gerado: {biblioteca.RelatorioPath}");
                            break;
                        case "7":
                            biblioteca.Salvar();
                            Console.WriteLine($"Dados salvos em {biblioteca.JsonPath}");
                            break;
                        case "0":
                            sair = true;
                            biblioteca.Salvar();
                            Console.WriteLine("Saindo... dados salvos.");
                            break;
                        default:
                            Console.WriteLine("Opção inválida.");
                            break;
                    }
                }
                catch (ArgumentException aex)
                {
                    // [AV1-5] tratamento de exceções coerente
                    Console.WriteLine($"Erro: {aex.Message}");
                    LogToFileSafe(aex);
                }
                catch (InvalidOperationException ioex)
                {
                    // [AV1-5]
                    Console.WriteLine($"Operação inválida: {ioex.Message}");
                    LogToFileSafe(ioex);
                }
                catch (Exception ex)
                {
                    // [AV1-5]
                    Console.WriteLine("Ocorreu um erro inesperado. Verifique debug.log.");
                    LogToFileSafe(ex);
                }
                Console.WriteLine();
            }
        }

        static void CadastrarJogoFlow(BibliotecaJogos biblioteca)
        {
            Console.Write("Nome do jogo: ");
            var nome = Console.ReadLine() ?? "";
            Console.Write("Categoria: ");
            var cat = Console.ReadLine() ?? "";
            try
            {
                var j = biblioteca.CadastrarJogo(nome, cat);
                Console.WriteLine($"Jogo cadastrado: #{j.Id} - {j.Nome}");
            }
            catch (Exception ex)
            {
                biblioteca.LogError(ex);
                Console.WriteLine($"Falha ao cadastrar: {ex.Message}");
            }
        }

        static void CadastrarMembroFlow(BibliotecaJogos biblioteca)
        {
            Console.Write("Nome do membro: ");
            var nome = Console.ReadLine() ?? "";
            Console.Write("Contato (telefone/email): ");
            var cont = Console.ReadLine() ?? "";
            try
            {
                var m = biblioteca.CadastrarMembro(nome, cont);
                Console.WriteLine($"Membro cadastrado: #{m.Id} - {m.Nome}");
            }
            catch (Exception ex)
            {
                biblioteca.LogError(ex);
                Console.WriteLine($"Falha ao cadastrar membro: {ex.Message}");
            }
        }

        static void ListarJogosFlow(BibliotecaJogos biblioteca)
        {
            Console.WriteLine("Lista de jogos:");
            foreach (var j in biblioteca.ListarJogos())
            {
                Console.WriteLine($"#{j.Id} - {j.Nome} - {j.Categoria} - Emprestado: {j.EstaEmprestado}");
            }
        }

        static void EmprestarFlow(BibliotecaJogos biblioteca)
        {
            try
            {
                Console.Write("Digite o id do jogo a emprestar: ");
                if (!int.TryParse(Console.ReadLine(), out var jogoId)) { Console.WriteLine("Id inválido."); return; }
                Console.Write("Digite o id do membro: ");
                if (!int.TryParse(Console.ReadLine(), out var membroId)) { Console.WriteLine("Id inválido."); return; }
                Console.Write("Dias para devolução (padrão 7): ");
                var diasStr = Console.ReadLine();
                int dias = 7;
                if (!string.IsNullOrWhiteSpace(diasStr)) int.TryParse(diasStr, out dias);

                var emp = biblioteca.EmprestarJogo(jogoId, membroId, DateTime.Now, dias);
                Console.WriteLine($"Empréstimo criado: #{emp.Id} - Prev. devolução: {emp.DataPrevistaDevolucao:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                biblioteca.LogError(ex);
                Console.WriteLine($"Falha ao emprestar: {ex.Message}");
            }
        }

        static void DevolverFlow(BibliotecaJogos biblioteca)
        {
            try
            {
                Console.Write("Digite o id do empréstimo: ");
                if (!int.TryParse(Console.ReadLine(), out var empId)) { Console.WriteLine("Id inválido."); return; }
                var emprestimo = biblioteca.Emprestimos.FirstOrDefault(e => e.Id == empId);
                if (emprestimo == null) { Console.WriteLine("Empréstimo não encontrado."); return; }
                if (emprestimo.EstaDevolvido) { Console.WriteLine("Já devolvido."); return; }

                var multa = emprestimo.CalcularMulta();
                Console.WriteLine($"Multa calculada: {multa:C2} (Dias de atraso: {emprestimo.DiasAtraso()})");
                Console.Write("Forma de pagamento (Pix/Dinheiro) - apenas registro: ");
                var forma = Console.ReadLine();
                Console.Write("Digite o valor pago: ");
                if (!decimal.TryParse(Console.ReadLine(), out var pago)) { Console.WriteLine("Valor inválido."); return; }

                var (emp, multaPago) = biblioteca.DevolverJogo(empId, DateTime.Now, pago);
                Console.WriteLine($"Devolução registrada. Multa: {multaPago:C2}");
            }
            catch (Exception ex)
            {
                biblioteca.LogError(ex);
                Console.WriteLine($"Falha ao devolver: {ex.Message}");
            }
        }

        // segurança para log (usa arquivo debug.log default)
        static void LogToFileSafe(Exception ex)
        {
            try
            {
                var lib = new BibliotecaJogos();
                lib.LogError(ex);
            }
            catch
            {
                // ignore
            }
        }
    }
}
