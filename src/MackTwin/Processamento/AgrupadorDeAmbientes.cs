using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MackTwin.Dominio;
using MackTwin.Parsing;

namespace MackTwin.Processamento
{
    /// <summary>
    /// Organiza leituras soltas em sensores e sensores em salas, usando o HOSTNAME como chave.
    /// </summary>
    /// <remarks>
    /// Duas convenções levam um sensor à sua sala (<see cref="ResolvedorDeSala"/>): o padrão
    /// <c>MMCCT&lt;sala&gt;-&lt;nnn&gt;</c>, em que <c>MMCCTLAB001-001</c> e <c>MMCCTLAB001-002</c>
    /// caem sozinhos em LAB001, e o HOSTNAME inteiro como código de um ambiente cadastrado à mão.
    /// Sensores que não casam com nenhuma não são perdidos — ficam em
    /// <see cref="ConjuntoDeAmbientes.SensoresSemSala"/>, com aviso. Ambientes cadastrados à mão
    /// entram mesmo sem sensor, vazios, para que o cadastro das planilhas deles tenha onde morar.
    /// Os totais do conjunto são calculados aqui, uma vez, e o HOSTNAME é analisado uma vez por
    /// sensor, não por leitura.
    /// </remarks>
    public static class AgrupadorDeAmbientes
    {
        /// <summary>Agrupa as leituras, vindas de uma ou várias planilhas, em salas.</summary>
        /// <param name="leituras">Leituras de qualquer origem, em qualquer ordem.</param>
        /// <param name="avisos">Recebe sensores sem sala e o resumo. Pode ser null.</param>
        /// <param name="salasCadastradas">
        /// Ambientes cadastrados à mão: código como digitado (o HOSTNAME do sensor, em qualquer
        /// grafia) e nome opcional. Pode ser null.
        /// </param>
        public static ConjuntoDeAmbientes Agrupar(
            IEnumerable<Leitura> leituras,
            List<string>? avisos = null,
            IEnumerable<(string Codigo, string? Nome)>? salasCadastradas = null)
        {
            var cadastradas = new List<(string Codigo, string CodigoExibido, string? Nome)>();
            var codigosCadastrados = new HashSet<string>(StringComparer.Ordinal);

            if (salasCadastradas != null)
            {
                foreach (var (codigoBruto, nome) in salasCadastradas)
                {
                    string codigo = CodigoDeSala.Normalizar(codigoBruto);

                    if (codigo.Length > 0 && codigosCadastrados.Add(codigo))
                        cadastradas.Add((codigo, codigoBruto.Trim(), string.IsNullOrWhiteSpace(nome) ? null : nome.Trim()));
                }
            }

            var sensoresPorHostname = new Dictionary<string, Sensor>(StringComparer.OrdinalIgnoreCase);
            int totalDeLeituras = 0;

            foreach (var leitura in leituras)
            {
                if (string.IsNullOrWhiteSpace(leitura.Hostname))
                    continue;

                string hostname = leitura.Hostname.Trim();

                if (!sensoresPorHostname.TryGetValue(hostname, out var sensor))
                {
                    bool noPadrao = AnalisadorDeHostname.TentarIdentificar(hostname, out _, out string dispositivo);

                    sensor = new Sensor
                    {
                        Hostname = hostname,
                        NumeroDoDispositivo = noPadrao ? dispositivo : null,
                        CodigoDaSala = ResolvedorDeSala.Resolver(hostname, codigosCadastrados),
                    };
                    sensoresPorHostname[hostname] = sensor;
                }

                sensor.Leituras.Add(leitura);
                totalDeLeituras++;

                if (!string.IsNullOrWhiteSpace(leitura.EnderecoMac))
                    sensor.EnderecoMac = leitura.EnderecoMac;
            }

            foreach (var sensor in sensoresPorHostname.Values)
                sensor.Leituras.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            var ambientes = new Dictionary<string, Ambiente>(StringComparer.Ordinal);

            // Primeiro as cadastradas à mão, para que nome e código exibido já estejam nelas.
            foreach (var (codigo, codigoExibido, nome) in cadastradas)
            {
                ambientes[codigo] = new Ambiente
                {
                    Codigo = codigo,
                    CodigoExibido = codigoExibido,
                    Apelido = nome,
                    CadastradaManualmente = true,
                };
            }

            var semSala = new List<Sensor>();

            foreach (var sensor in sensoresPorHostname.Values.OrderBy(s => s.Hostname, StringComparer.OrdinalIgnoreCase))
            {
                if (sensor.CodigoDaSala.Length == 0)
                {
                    semSala.Add(sensor);
                    avisos?.Add(
                        $"Sensor \"{sensor.Hostname}\" não segue o padrão MMCCT<sala>-<nnn> nem é o código de um " +
                        "ambiente cadastrado; não foi associado a nenhuma sala.");
                    continue;
                }

                if (!ambientes.TryGetValue(sensor.CodigoDaSala, out var ambiente))
                {
                    ambiente = new Ambiente { Codigo = sensor.CodigoDaSala };
                    ambientes[sensor.CodigoDaSala] = ambiente;
                }

                ambiente.AdicionarSensor(sensor);
            }

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "{0} sensor(es) em {1} sala(s), {2} sem sala; {3} leitura(s) no total.",
                sensoresPorHostname.Count, ambientes.Count, semSala.Count, totalDeLeituras));

            return new ConjuntoDeAmbientes
            {
                Ambientes = ambientes.Values.OrderBy(a => a.ChaveDoRoom, StringComparer.Ordinal).ToList(),
                SensoresSemSala = semSala,
                AtualizadoEm = DateTime.Now,
                Avisos = avisos ?? new List<string>(),
                TotalDeLeituras = totalDeLeituras,
                TotalDeSensores = sensoresPorHostname.Count,
            };
        }
    }
}
