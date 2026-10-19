# ISO/IEC 27002:2022: controles sobre desenvolvimento de software

Fonte: ISO/IEC 27002:2022(E), 3ª edição, fevereiro de 2022, "Information security, cybersecurity and privacy protection — Information security controls". Texto enviado pelo Pedro em 19/10/2026 (`/mnt/project-files/uploads/hearth/7288167b-0b76-41ca-8b44-19c186246b4a`). Resumo em português feito em 19/10/2026; não é transcrição. A versão brasileira é a ABNT NBR ISO/IEC 27002:2022.

Natureza: a 27002 é guia de controles (verbo "convém", *should*). Ela vira exigência quando a política da organização a adota ou quando a ISO/IEC 27001:2022 é aplicada (Anexo A da 27001 lista os mesmos 93 controles). A DPC e a PS da AC Defesa são as exigências próprias da AC (ver referencias-verificadas.md).

Estrutura: 93 controles em 4 temas (5 organizacionais, 6 pessoas, 7 físicos, 8 tecnológicos). Os de desenvolvimento estão quase todos no tema 8.

## Mapa rápido

| Controle | Título | Ideia central | Especialista |
|---|---|---|---|
| 5.8 | Segurança da informação em gestão de projetos | Requisitos e riscos de segurança tratados desde o início e revistos em marcos | 9 e 10 |
| 8.4 | Acesso ao código-fonte | Leitura ampla, escrita restrita, mudança só via controle de mudanças, trilha de auditoria; código publicado com assinatura digital | 10 |
| 8.8 | Gestão de vulnerabilidades técnicas | Inventário de software e bibliotecas, fontes de alerta, prazo de reação, testar e aplicar correções, rastrear bibliotecas de terceiros | 10 |
| 8.9 | Gestão de configuração | Configurações definidas, documentadas, implantadas e monitoradas (base de hardening) | 10 |
| 8.24 | Uso de criptografia | Política de criptografia, algoritmos e força aprovados, gestão de chaves, papéis | 10 |
| 8.25 | Ciclo de vida de desenvolvimento seguro | Regras de desenvolvimento seguro estabelecidas e aplicadas | 9 e 10 |
| 8.26 | Requisitos de segurança de aplicações | Requisitos identificados, especificados e aprovados, normalmente a partir de análise de risco | 10 |
| 8.27 | Arquitetura segura e princípios de engenharia | Princípios documentados (security by design, defesa em profundidade, falhar com segurança, mínimo privilégio, zero trust) | 10 |
| 8.28 | Codificação segura | Linha de base mínima, padrões por linguagem, SAST, revisão, gestão de bibliotecas externas | 10 (9 na parte de qualidade) |
| 8.29 | Testes de segurança no desenvolvimento e na aceitação | Plano de testes proporcional ao risco, revisão de código, varredura, teste de intrusão, aceite independente | 9 e 10 |
| 8.30 | Desenvolvimento terceirizado | Exigências contratuais, evidências, direito de auditar, depósito do código (*escrow*) | 10 |
| 8.31 | Separação de ambientes | Desenvolvimento, teste e produção separados; ninguém muda dev e produção sozinho sem revisão | 9 |
| 8.32 | Gestão de mudanças | Planejar, autorizar, testar, implantar com plano de volta e registrar | 9 |
| 8.33 | Informação de teste | Não copiar dado sensível para teste; se copiar, autorizar, mascarar, registrar e apagar | 9 e 10 |
| 8.34 | Proteção dos sistemas durante testes de auditoria | Testes de auditoria planejados, preferencialmente só leitura | 10 |

## Resumo por controle

### 5.8 Segurança em gestão de projetos
- Riscos de segurança avaliados cedo e periodicamente como riscos do projeto; requisitos de segurança (inclusive 8.26 e propriedade intelectual, 5.32) tratados no início.
- Acompanhamento em marcos definidos por pessoa ou instância adequada; papéis de segurança no projeto definidos.
- Requisitos podem vir de política, regulação, modelagem de ameaças, análise de incidentes e planos de contingência. Considerar classificação da informação, nível de confiança na identidade (autenticação), concessão de acesso, registro de transações e não repúdio, obrigações legais.

### 8.4 Acesso ao código-fonte
- Controlar código, documentação de projeto e ferramentas de desenvolvimento (compiladores, build, integração, testes), de preferência num sistema de controle de versão central.
- Leitura pode ser ampla dentro da organização; escrita só para pessoal autorizado ou donos designados.
- Atualização do código só por gestão de mudanças (8.32) e após autorização; acesso via ferramentas que controlam as ações; log de todos os acessos e mudanças.
- Código que será publicado: considerar controles de integridade, como assinatura digital.
- Na prática em GitHub: proteção de branch, revisão obrigatória de PR, commits assinados, menor número de pessoas com permissão de escrita, segredos fora do repositório.

### 8.8 Gestão de vulnerabilidades técnicas
- Pré-requisito: inventário com fornecedor, nome, versão e onde está instalado cada software, inclusive bibliotecas de terceiros (alinha com SBOM).
- Fontes de alerta definidas, ferramentas de varredura, testes de intrusão planejados por pessoas competentes e autorizadas; rastrear bibliotecas de terceiros (liga com 8.28).
- Canal para receber relatos de vulnerabilidade; prazo definido para reagir; priorizar o que é de alto risco; usar só atualizações de fontes legítimas; testar antes de aplicar; mecanismo para verificar a autenticidade da correção.
- Sem correção disponível: contornos do fabricante, desligar a função, filtros, mais monitoramento.
- Registrar todos os passos.
- Aplicação em C#: `dotnet list package --vulnerable --include-transitive`, NuGet Audit no build, Dependabot, acompanhamento das versões de suporte do .NET (ex.: fim de suporte do .NET 8 em 10/11/2026, ver referencias-verificadas.md).

### 8.9 Gestão de configuração
- Configurações de hardware, software, serviços e redes definidas, documentadas, implantadas, monitoradas e revistas. É a base do hardening: modelo padrão endurecido, mudanças controladas, comparação periódica com a configuração aprovada (a DPC da AC Defesa, item 6.6.2.1, prevê verificação semanal dos níveis de segurança configurados).

### 8.24 Uso de criptografia
- Política específica de criptografia; nível de proteção conforme a classificação da informação; algoritmos, força, soluções e práticas aprovadas; gestão de chaves (geração, proteção, recuperação em caso de perda ou comprometimento); papéis definidos; impacto da cifração sobre controles que inspecionam conteúdo (antimalware, filtros).
- Na ICP-Brasil, algoritmos e tamanhos vêm do DOC-ICP-01.01 (ver normas-icp-brasil.md e especialistas 6 e 8).

### 8.25 Ciclo de vida de desenvolvimento seguro
Itens a cobrir:
- a) separação de ambientes (8.31);
- b) segurança na metodologia e guias de codificação segura para cada linguagem usada (8.27, 8.28);
- c) requisitos de segurança na especificação e no projeto (5.8);
- d) pontos de verificação de segurança no projeto (5.8);
- e) testes de sistema e de segurança: regressão, varredura de código, teste de intrusão (8.29);
- f) repositórios seguros para código e configuração (8.4, 8.9);
- g) segurança no controle de versão (8.32);
- h) conhecimento e treinamento em segurança de aplicações (8.28);
- i) capacidade dos desenvolvedores de prevenir, achar e corrigir vulnerabilidades (8.28);
- j) licenciamento (5.32).
Desenvolvimento terceirizado: obter garantia de que o fornecedor segue essas regras (8.30).

### 8.26 Requisitos de segurança de aplicações
Requisitos definidos por análise de risco, com apoio de especialistas em segurança. Lista do que considerar: confiança na identidade (autenticação), classificação da informação processada, segregação de acesso, resiliência a ataques (ex.: estouro de buffer, injeção de SQL), requisitos legais, privacidade, proteção de dados em processamento, trânsito e repouso, cifração das comunicações, validação de entrada e verificações de integridade, controles automáticos (dupla aprovação), controle de saída, campos de texto livre, registro de transações e não repúdio, integração com log e prevenção de vazamento, tratamento de mensagens de erro.
Para serviços transacionais: confiança mútua na identidade, integridade (hash, assinatura digital), quem pode assinar documentos, prova de envio e recebimento. Quando há autoridade confiável (emissão de certificados e assinaturas), a segurança deve estar integrada a todo o processo de gestão do certificado ou da assinatura. Aprofundamento: série ISO/IEC 27034.

### 8.27 Arquitetura segura e princípios de engenharia
- Princípios documentados e aplicados em todas as camadas; novas tecnologias analisadas contra padrões de ataque conhecidos.
- Princípios citados: *security by design*, defesa em profundidade, *security by default*, negar por padrão, falhar com segurança, desconfiar de entrada externa, segurança na implantação, assumir violação, mínimo privilégio, usabilidade e gerenciabilidade, mínima funcionalidade.
- Revisão de projeto orientada a segurança; registro formal de controles que não atendem plenamente; hardening dos sistemas.
- Zero trust: não confiar só no perímetro, verificar sempre, cifrar ponta a ponta, autenticar e autorizar cada requisição.
- Considerar infraestrutura de segurança existente, como PKI e gestão de identidades.

### 8.28 Codificação segura
- Governança de codificação segura na organização com **linha de base mínima**, estendida a componentes de terceiros e código aberto; acompanhar ameaças reais e alertas atualizados.
- Antes de codificar: princípios aprovados (internos e terceirizados), defeitos históricos que geram vulnerabilidades, IDE configurada para ajudar, seguir orientação dos fornecedores das ferramentas e do ambiente de execução, ferramentas atualizadas (compiladores), qualificação dos desenvolvedores, projeto seguro com **modelagem de ameaças**, padrões de codificação segura, ambiente controlado.
- Durante: práticas específicas da linguagem; programação em par, refatoração, revisão por pares, TDD; programação estruturada; documentação; **proibir** senhas fixas no código, exemplos de código não aprovados e serviços web sem autenticação. SAST durante e depois.
- Antes de pôr em produção: avaliar superfície de ataque e mínimo privilégio; analisar os erros de programação mais comuns e documentar que foram mitigados (OWASP Top 10, CWE Top 25 servem de lista).
- Depois: atualizações empacotadas e implantadas com segurança; tratar vulnerabilidades relatadas (8.8); registrar erros e ataques suspeitos e revisar os logs; proteger o código contra acesso e adulteração.
- Bibliotecas externas: inventário com versões e atualização regular; preferir componentes consagrados, **sobretudo de autenticação e criptografia**; avaliar licença, segurança e histórico; origem confiável; disponibilidade de longo prazo.
- Modificar pacote de terceiros: risco aos controles embutidos, consentimento do fornecedor, manutenção futura, compatibilidade.
- Princípio-guia: o código de segurança deve ser sempre chamado quando necessário e ser resistente a adulteração; projetar supondo que o código estará sempre sob ataque. Avaliação de segurança de produtos: série ISO/IEC 15408 (Common Criteria).

### 8.29 Testes de segurança no desenvolvimento e na aceitação
- Testar funções de segurança (autenticação, controle de acesso, criptografia), codificação segura e configurações seguras.
- Plano de testes proporcional à importância do sistema e ao impacto da mudança: cronograma, entradas e saídas esperadas em várias condições, critérios de avaliação, decisão sobre próximos passos.
- Ferramentas automáticas (análise de código, varredura de vulnerabilidades) e verificação de que os defeitos foram corrigidos.
- Primeiro testa a equipe de desenvolvimento; depois **aceite independente** para garantir que o sistema faz o esperado e só o esperado: revisão de código incluindo entradas inesperadas, varredura de vulnerabilidades, teste de intrusão.
- Ambiente de teste o mais parecido possível com produção.

### 8.30 Desenvolvimento terceirizado
Licenças e propriedade do código, requisitos contratuais de projeto, codificação e teste seguros, modelo de ameaças fornecido ao terceiro, teste de aceite, evidências de segurança e de testes contra conteúdo malicioso e vulnerabilidades conhecidas, depósito do código-fonte, direito de auditar, requisitos do ambiente de desenvolvimento, legislação.

### 8.31 Separação de ambientes
- Desenvolvimento e produção em domínios separados; regras e autorização para promover software a produção; testar antes em homologação; não testar em produção salvo exceção aprovada; ferramentas de desenvolvimento ausentes da produção; identificação visual do ambiente; não copiar dado sensível para dev e teste sem controles equivalentes.
- Proteger os próprios ambientes de dev e teste: atualizar ferramentas, configuração segura, controle de acesso, monitorar mudanças, backup.
- Uma só pessoa não deve conseguir mudar desenvolvimento e produção sem revisão e aprovação prévias.
- A DPC da AC Defesa v4.0, item 6.6.1.1, já prevê desenvolvimento, homologação e produção, com decisão do Chefe da AC Principal para implantar.

### 8.32 Gestão de mudanças
Procedimento formal para todo o ciclo: planejar e avaliar impacto e dependências, autorizar, comunicar, testar e aceitar (8.29), implantar com plano, prever volta atrás (*fall-back*), registrar tudo, atualizar documentação operacional e planos de continuidade.

### 8.33 Informação de teste
Não copiar dado sensível (inclusive pessoal) para teste. Se for necessário: mesmo controle de acesso da produção, autorização a cada cópia, registro, remoção ou mascaramento (8.11) e exclusão logo após o teste (8.10). Em aplicativos de cifração, usar certificados e chaves de laboratório (ex.: SoftHSM, cadeia de teste), nunca certificados reais de usuários.

### 8.34 Proteção durante testes de auditoria
Escopo combinado com a gestão, acesso preferencialmente só leitura, cópias isoladas, testes que afetam disponibilidade fora do horário, tudo registrado.

## Atributos úteis (Anexo A da 27002)
Todos os controles 8.25 a 8.33 têm o atributo #Application_security. Para montar uma visão "segurança de aplicações", filtrar por esse atributo. O Anexo B traz a correspondência com a edição de 2013 (ex.: 8.28 Codificação segura é controle novo em 2022).
