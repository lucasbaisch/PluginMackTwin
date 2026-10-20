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
    /// É aqui que o achado-chave vira estrutura: <c>MMCCTLAB001-001</c> e <c>MMCCTLAB001-002</c>
    /// são dois sensores da mesma sala LAB001, sem que ninguém precise dizer isso. Sensores com
    /// nome fora do padrão não são perdidos — ficam em <see cref="ConjuntoDeAmbientes.SensoresSemSala"/>,
    /// com aviso, para que alguém os renomeie. Salas cadastradas à mão entram mesmo sem sensor,
    /// vazias, para que o cadastro das planilhas delas tenha onde morar. Os totais do conjunto
    /// são calculados aqui, uma vez, e o HOSTNAME é analisado uma vez por sensor, não por leitura.
    /// </remarks>
    public static class AgrupadorDeAmbientes
    {
        /// <summary>Agrupa as leituras, vindas de uma ou várias planilhas, em salas.</summary>
        /// <param name="leituras">Leituras de qualquer origem, em qualquer ordem.</param>
        /// <param name="avisos">Recebe sensores sem sala e o resumo. Pode ser null.</param>
        /// <param name="salasCadastradas">Salas cadastradas à mão (código em qualquer grafia, nome opcional). Pode ser null.</param>
        public static ConjuntoDeAmbientes Agrupar(
            IEnumerable<Leitura> leituras,
            List<string>? avisos = null,
            IEnumerable<(string Codigo, string? Nome)>? salasCadastradas = null)
        {
            var sensoresPorHostname = new Dictionary<string, Sensor>(StringComparer.OrdinalIgnoreCase);
            int totalDeLeituras = 0;

            foreach (var leitura in leituras)
            {
                if (string.IsNullOrWhiteSpace(leitura.Hostname))
                    continue;

                string hostname = leitura.Hostname.Trim();

                if (!sensoresPorHostname.TryGetValue(hostname, out var sensor))
                {
                    AnalisadorDeHostname.TentarIdentificar(hostname, out var identificacao);

                    sensor = new Sensor { Hostname = hostname, Identificacao = identificacao };
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
            var semSala = new List<Sensor>();

            foreach (var sensor in sensoresPorHostname.Values.OrderBy(s => s.Hostname, StringComparer.OrdinalIgnoreCase))
            {
                if (sensor.Identificacao == null)
                {
                    semSala.Add(sensor);
                    avisos?.Add(
                        $"Sensor \"{sensor.Hostname}\" não segue o padrão de nome (ex.: MMCCTLAB001-001) " +
                        "e não foi associado a nenhuma sala.");
                    continue;
                }

                string codigo = sensor.Identificacao.CodigoDaSala;

                if (!ambientes.TryGetValue(codigo, out var ambiente))
                {
                    ambiente = new Ambiente { Codigo = codigo };
                    ambientes[codigo] = ambiente;
                }

                ambiente.AdicionarSensor(sensor);
            }

            if (salasCadastradas != null)
            {
                foreach (var (codigoBruto, nome) in salasCadastradas)
                {
                    string codigo = CodigoDeSala.Normalizar(codigoBruto);

                    if (codigo.Length == 0)
                        continue;

                    if (!ambientes.TryGetValue(codigo, out var ambiente))
                    {
                        ambiente = new Ambiente { Codigo = codigo };
                        ambientes[codigo] = ambiente;
                    }

                    ambiente.CadastradaManualmente = true;

                    if (!string.IsNullOrWhiteSpace(nome))
                        ambiente.Apelido = nome;
                }
            }

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "{0} sensor(es) em {1} sala(s), {2} sem sala; {3} leitura(s) no total.",
                sensoresPorHostname.Count, ambientes.Count, semSala.Count, totalDeLeituras));

            return new ConjuntoDeAmbientes
            {
                Ambientes = ambientes.Values.OrderBy(a => a.Codigo, StringComparer.Ordinal).ToList(),
                SensoresSemSala = semSala,
                AtualizadoEm = DateTime.Now,
                Avisos = avisos ?? new List<string>(),
                TotalDeLeituras = totalDeLeituras,
                TotalDeSensores = sensoresPorHostname.Count,
            };
        }
    }
}
