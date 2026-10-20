using System.Collections.Generic;

namespace MackTwin.Dominio
{
    /// <summary>
    /// Uma sala do CCT e os sensores instalados nela.
    /// </summary>
    /// <remarks>
    /// Nasce das planilhas (todo HOSTNAME reconhecido cria ou alimenta um ambiente) ou do
    /// cadastro à mão, e, quando há modelo aberto, é casada com um Room pela
    /// <see cref="ChaveDoRoom"/>: o nome dado no cadastro, ou o código quando não há nome.
    /// O vínculo com o modelo é guardado como o valor numérico do <c>ElementId</c>, e não como o
    /// tipo do Revit, para que o domínio continue verificável fora do Revit.
    ///
    /// <b>As leituras consolidadas são calculadas uma vez.</b> Juntar e ordenar as leituras de
    /// todos os sensores custa O(n log n); o resumo, o gráfico e a pintura pedem essa lista
    /// várias vezes por ciclo. Por isso ela é montada no primeiro acesso e guardada, e só é
    /// invalidada quando um sensor entra — o que só acontece ao montar o conjunto.
    /// </remarks>
    public sealed class Ambiente
    {
        private readonly List<Sensor> _sensores = new();
        private List<Leitura>? _leituras;
        private string? _codigoExibido;

        /// <summary>Código normalizado da sala (ex.: LAB001). Chave do ambiente.</summary>
        public string Codigo { get; init; } = string.Empty;

        /// <summary>Código como foi cadastrado (com hífens e sublinhados), para exibição; o próprio código se não houver.</summary>
        public string CodigoExibido
        {
            get => string.IsNullOrWhiteSpace(_codigoExibido) ? Codigo : _codigoExibido;
            set => _codigoExibido = value;
        }

        /// <summary>Sensores da sala, na ordem em que foram adicionados (por HOSTNAME).</summary>
        public IReadOnlyList<Sensor> Sensores => _sensores;

        /// <summary>Valor do ElementId do Room casado no modelo, ou null se não casou.</summary>
        public long? IdDoElementoNoModelo { get; set; }

        /// <summary>Nome do Room no modelo (ex.: "Laboratório de Informática"), se casou.</summary>
        public string? NomeNoModelo { get; set; }

        /// <summary>Número do Room no modelo, como digitado pelo projetista, se casou.</summary>
        public string? NumeroNoModelo { get; set; }

        /// <summary>Se há um Room correspondente no modelo aberto.</summary>
        public bool CasadoComOModelo => IdDoElementoNoModelo.HasValue;

        /// <summary>Nome dado no cadastro manual do ambiente — é o que casa com o Room.</summary>
        public string? Apelido { get; set; }

        /// <summary>Se a sala foi cadastrada à mão (e não só anunciada por um sensor).</summary>
        public bool CadastradaManualmente { get; set; }

        /// <summary>
        /// O que se compara com o Número/Nome do Room, já normalizado: o nome do cadastro, ou o
        /// código quando a sala não tem nome (caso das anunciadas pela convenção do HOSTNAME).
        /// </summary>
        public string ChaveDoRoom
        {
            get
            {
                string peloNome = CodigoDeSala.Normalizar(Apelido);
                return peloNome.Length > 0 ? peloNome : Codigo;
            }
        }

        /// <summary>
        /// Acrescenta um sensor. As leituras do sensor devem estar completas e em ordem
        /// cronológica — é assim que o agrupador as entrega.
        /// </summary>
        public void AdicionarSensor(Sensor sensor)
        {
            _sensores.Add(sensor);
            _leituras = null;
        }

        /// <summary>Todas as leituras de todos os sensores, em ordem cronológica (consolidadas uma vez).</summary>
        public IReadOnlyList<Leitura> Leituras => _leituras ??= Consolidar();

        /// <summary>A leitura mais recente entre todos os sensores, ou null.</summary>
        public Leitura? UltimaLeitura
        {
            get
            {
                var leituras = Leituras;
                return leituras.Count == 0 ? null : leituras[leituras.Count - 1];
            }
        }

        /// <summary>
        /// Título para a interface: o nome (do Room, se casou; senão o do cadastro) e o código;
        /// só o código quando não há nome.
        /// </summary>
        public string Titulo
        {
            get
            {
                string? nome = !string.IsNullOrWhiteSpace(NomeNoModelo) ? NomeNoModelo : Apelido;
                return string.IsNullOrWhiteSpace(nome) ? CodigoExibido : $"{nome} — {CodigoExibido}";
            }
        }

        private List<Leitura> Consolidar()
        {
            if (_sensores.Count == 1)
                return new List<Leitura>(_sensores[0].Leituras);

            int total = 0;

            foreach (var sensor in _sensores)
                total += sensor.Leituras.Count;

            var todas = new List<Leitura>(total);

            foreach (var sensor in _sensores)
                todas.AddRange(sensor.Leituras);

            todas.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return todas;
        }

        public override string ToString() => Titulo;
    }
}
