# Base de desenvolvimento de software e segurança de código

Base dos especialistas 9 (desenvolvimento C#/.NET, qualidade e desempenho) e 10 (segurança de código e hardening) do conselho. Criada em 19/10/2026.

São resumos em português, feitos para consulta rápida. A fonte primária vale sobre o resumo: antes de citar versão ou data num parecer, o especialista confere a fonte e diz a data da consulta.

| Arquivo | Conteúdo |
|---|---|
| [iso-27002-2022-desenvolvimento.md](iso-27002-2022-desenvolvimento.md) | Controles da ISO/IEC 27002:2022 sobre desenvolvimento (5.8, 8.4, 8.8, 8.9, 8.24 a 8.34), com o que pedem e como aplicar em C# |
| [referencias-verificadas.md](referencias-verificadas.md) | Microsoft (.NET, C#), OWASP, CWE, NIST SSDF e os itens da DPC e da PS da AC Defesa sobre desenvolvimento, com versão, data e fonte conferidas em 19/10/2026 |
| [fontes-indicadas.md](fontes-indicadas.md) | As três fontes indicadas pelo Pedro (modelos ISO 27001, skills de GRC, artigo de boas práticas C#): o que têm, o que aproveitar e limites |

## Onde estão os originais

- ISO/IEC 27002:2022 (texto enviado pelo Pedro em 19/10/2026): `/mnt/project-files/uploads/hearth/7288167b-0b76-41ca-8b44-19c186246b4a` (texto extraído do PDF, em inglês, 8.442 linhas). Documento protegido por direitos autorais da ISO; usar só para consulta interna.
- Normas de hardening em C# e de desempenho: o Pedro vai colocá-las no repositório github.com/pkrezende/normas_tecnicas, pasta `normas-desenvolvimento/`. Quando chegarem, registrar cada uma na tabela acima (nome, versão, data, o que cobre).
- DPC v4.0 e PS v2.0 da AC Defesa: pasta `normas-AC-Defesa/` e raiz do Projeto.

## Como usar num parecer

1. Requisito: citar ISO/IEC 27002:2022 controle X, DPC v4.0 item Y ou PS v2.0 item Z. A ISO 27002 é guia de boas práticas (verbo "convém"); só vira exigência quando uma política da organização a adota. A DPC e a PS da AC Defesa são exigência para a AC e suas ARs.
2. Boa prática: OWASP, CWE, NIST SSDF, diretrizes Microsoft. Citar versão e data.
3. Opinião do especialista: rotular como tal.

## Nesta pasta do repositório

Coloque aqui as normas de hardening em C# e de desempenho (PDF, Markdown ou link). Os resumos acima são cópia da base do Projeto (`/mnt/project-files/normas-desenvolvimento/`), que é a versão de trabalho do conselho. O texto da ISO/IEC 27002 não é publicado aqui por ser protegido por direitos autorais.
