# Levantamento de melhorias — BZ.Howly.Api

Data: 2026-10-14 · Escopo: Core, Services, Utils, DAO, API e configuração. Testes fora do escopo (por decisão).
Severidade: **Alta / Média / Baixa** · Esforço: **P** (horas) / **M** (1–2 dias) / **G** (vários dias).
✔ = conferido manualmente no código, além da revisão automatizada.

> Material de apoio: valide cada item antes de aplicar.

---

## Decisões de regra de negócio (2026-10-14)

| # | Pergunta | Decisão | Onde |
|---|---|---|---|
| Q1 | Documento **aprovado** pode voltar para revisão? | ✅ **Só o analista** pode. O cliente nunca. | `Core/Processes/ProcessDocument.cs:117` |
| Q2 | Cliente pode enviar documento de etapa futura/concluída? | ✅ **Não.** O cliente só anexa arquivos quando a **etapa atual é dele**; fora isso, só aguarda. | `Services/Processes/ProcessDocumentService.cs:31-47` |
| Q3 | Analista reseta senha de cliente e vê a senha temporária? | ✅ O analista dispara o reset, mas a senha vai **direto para o e-mail/WhatsApp do cliente**, sem passar por ninguém interno. Depende de G1 (canal de envio). | `CustomersController.cs:61-65`, `CustomerService.cs:132-149` |
| Q4 | Processo concluído/cancelado pode ser reatribuído? | ⏳ Em aberto. No futuro: reabrir processo ou resetar etapa (ver G3). | `Core/Processes/Process.cs:178` |
| Q5 | Pausa congela o prazo? | ✅ **Opção 3:** a pausa congela tudo (etapas, documentos e prazo). Só analista/admin retomam ou cancelam. Comentários e notas internas continuam permitidos. | `Core/Processes/Process.cs`, `ProcessParts.cs` |
| — | Comentários | ✅ Cliente e equipe comentam a qualquer momento com o processo aberto (inclusive pausado); bloqueados quando **concluído ou cancelado**. | `Process.Comment` |

## Progresso

| Recorte | Status | Observação |
|---|---|---|
| **A** (2026-10-14) | ✅ Feito, exceto A11 | Migration `ProcessRulesAndConcurrency`. A11 (dias úteis/feriados/fuso) aguarda decisão de regra. Testes novos em `ProcessRulesTests`, `ProcessTypeRulesTests` e `ProcessRulesEndpointsTests`. |

---

## A. Bugs e regras de negócio

| # | Sev | Esf | Item | Onde |
|---|---|---|---|---|
| A1 ✔ | Alta | M | Prazo da etapa continua correndo durante a pausa → ao retomar já está vencido e dispara aviso | `ProcessParts.cs:90`, `Process.cs:155-164` |
| A2 | Alta | M | Sem concorrência otimista (`Version` + `IsConcurrencyToken`) e sem índice único em `(ProcessId, Sequence)` / `(DocumentId, Number)` → "lost update" e versões duplicadas | `Process`, `ProcessDocument`, `ProcessConfiguration.cs`, `ProcessDocumentConfiguration.cs` |
| A3 | Média | M | Subprocesso: pode ser aberto sob pai concluído/cancelado; pai pode encerrar com filhos abertos. Regra só no service | `Process.cs:58-81`, `ProcessService.cs:57` |
| A4 | Média | P | Aprovar/rejeitar documento em processo concluído/cancelado (Upload/Add bloqueiam, Review não) | `ProcessDocumentService.cs:109-137` |
| A5 | Média | M | `ReturnTo` não reabre os documentos das etapas reabertas (seguem "aprovados") | `Process.cs:127-141` |
| A6 ✔ | Média | P | Nome de arquivo com extensão > 200 caracteres → `ArgumentOutOfRange` → 500 | `Core/Shared/UploadPolicy.cs:77` |
| A7 ✔ | Média | P | Entregável adicionado com processo **pausado** vai para a última etapa (`ActiveStepId` é null na pausa) | `ProcessDocumentService.cs:77` |
| A8 | Média | P | `Assign`: sem guarda de status, sem no-op (mesmo responsável gera evento), aceita `Guid.Empty`; **responsável anterior não é notificado** | `Process.cs:178-183`, `NotificationService.cs:23` |
| A9 ✔ | Média | P | `AddDocument`: comentário diz que `kind` padrão é Extra, mas o enum padrão é `Requested` → 400 | `ProcessesController.cs:93` |
| A10 | Média | M | Skip não registra documentos pendentes; falta status "Dispensado" (Waived) — pendências ficam eternamente aguardando | `Process.cs:88-124` |
| A11 | Média | M | Prazos em dias corridos somados a UTC; sem dias úteis, feriados e fuso America/Sao_Paulo | `ProcessParts.cs:90` |
| A12 | Baixa | P | Cancelar deixa a etapa ativa com status `Active` (concluir fecha todas) | `Process.cs:166-176` |
| A13 | Baixa | P | Validações de tipo de processo: `Owner` ausente vira `Customer`; `null` em arrays → 500; etapa de Órgão sem nome do órgão; nomes de etapa e de tipo duplicados | `ProcessTypeRequest.cs:17`, `ProcessType.cs:42-63`, `ProcessTypeConfiguration.cs:16` |
| A14 ✔ | Baixa | P | Mensagem do filtro aponta rota inexistente `/api/me/profile/confirm` (real: `/api/me/company/confirm`) | `ProfileConfirmationRequiredFilter.cs:32` |
| A15 | Baixa | P | `StaffController` pega 500 contas (incl. inativas) e filtra em memória; único controller que fala direto com o DAO | `StaffController.cs:19-26` |

## B. Segurança

| # | Sev | Esf | Item | Onde |
|---|---|---|---|---|
| B1 | Média | P | Reset/troca de senha e revogação dos refresh tokens salvos **fora de transação** → falha no 2º save deixa tokens do invasor válidos | `CustomerService.cs:140-144`, `InternalUserService.cs:179-181`, `AuthService.cs:77-87` |
| B2 | Média | M | Access token **não é invalidado** por troca/reset de senha (sem security stamp no token) | `JwtTokenService.cs:39-49`, `ServiceCollectionExtensions.cs:132-146` |
| B3 ✔ | Média | M | Refresh sem janela de tolerância: duas abas renovando juntas = "reuso" → derruba a sessão em todos os dispositivos | `AuthService.cs:37-43` |
| B4 | Média | M | Corrida na regra "último Admin ativo" + contagem carrega todos os usuários em memória | `UserAccountStore.cs:244`, `InternalUserService.cs:94-118` |
| B5 | Média | P | Tempo de resposta do login revela se o CNPJ é cliente (sem hash "dummy" quando o usuário não existe) | `AuthService.cs:97-103` |
| B6 | Baixa | P | `[AllowPendingPasswordChange]` na classe inteira: com senha temporária dá para alterar perfil/avatar | `MeController.cs:15-16` |
| B7 | Baixa | M | Sessão sem idade máxima absoluta (renovando a cada 7 dias, vive para sempre) | `AuthService.cs:118` |
| B8 | Baixa | P | Hardening: headers (`nosniff`, `Referrer-Policy`), `no-store` nas respostas com token/senha temporária, `AllowInputFormatterExceptionMessages=false`, `AllowedHosts`, header `Server` | `Program.cs`, `appsettings.json` |
| B9 | Baixa | P | Rate limit: `change-password` divide o balde de login por IP; sem `ForwardedHeaders` atrás de proxy | `ServiceCollectionExtensions.cs:156-164` |
| B10 | Baixa | P | LGPD: logs com CNPJ e nome de arquivo enviado → logar só IDs | `CustomerService.cs:52`, `ProcessDocumentService.cs:158` |
| B11 | Baixa | P | Se usar `SameSite=None`, falta checar `Origin` em `/refresh` e `/logout` | `RefreshCookieOptions.cs`, `AuthController.cs:60-70` |

## C. Performance e dados

| # | Sev | Esf | Item | Onde |
|---|---|---|---|---|
| C1 | Alta | P→G | Carregar um processo traz **etapas × eventos** (produto cartesiano, eventos crescem sem limite). Curto prazo: `AsSplitQuery`. Ideal: eventos fora do agregado | `ProcessConfiguration.cs:36-66`, `ProcessRepository.cs` |
| C2 | Média | M | `ToResponseAsync` faz ~10 consultas e recarrega o processo/pai inteiro só para o título | `ProcessService.cs:199-230`, `ProcessDocumentService.cs:185` |
| C3 | Média | M | Worker de vencidos carrega **todos** os vencidos (inclusive já avisados) + N+1 de clientes | `OverdueStepService.cs:16`, `ProcessRepository.ListOverdueAsync` |
| C4 | Média | P | Detalhe/desativação de usuário interno carrega processos completos só para contar | `InternalUserService.cs:121,168-170` |
| C5 | Média | P | Índices faltando: `UpdatedAt`, `(CustomerId, UpdatedAt)`, `ActiveStepDueAt` (filtrado), notificações `(UserId, CreatedAt)` | `ProcessConfiguration.cs:26-29`, `NotificationConfiguration.cs:23` |
| C6 | Média | P | Paginação sem desempate estável (`ThenBy(Id)`) → itens repetidos/pulados entre páginas | `ProcessRepository.cs:61`, `CustomerRepository.cs:52`, `UserAccountStore.cs:47`, `NotificationRepository.cs:19` |
| C7 | Média | M | GUID aleatório como chave clusterizada → fragmentação (eventos, notificações, tokens, versões) | todas as configurations |
| C8 | Média | M | Sem retenção/limpeza: refresh tokens e notificações crescem para sempre (LGPD + performance) | `RefreshTokenRepository.cs`, `NotificationRepository.cs` |
| C9 | Baixa | P | Sem índice único `(UserId, EventId)` em notificações → duplicata quebra a timeline (`ToDictionary`) com 500 | `NotificationRepository.cs:36-39` |
| C10 | Baixa | P | Cascade delete apagaria todo o histórico de um processo → usar `Restrict` + arquivamento | `NotificationConfiguration.cs:27`, `ProcessDocumentConfiguration.cs:23` |
| C11 | Baixa | P | Busca com `ToUpper()` em cada linha (SQL Server já é case-insensitive) | `ProcessRepository.cs:53-56` e outros |

## D. Arquitetura e design

| # | Sev | Esf | Item | Onde |
|---|---|---|---|---|
| D1 | Média | P | **Arquivos órfãos**: grava no disco antes do `SaveChanges`; cópia parcial em falha; avatar concorrente; `File.Exists` + open com corrida | `ProcessDocumentService.cs`, `AccountProfileService.cs:54-61,106`, `DiskFileStorage.cs:35-45` |
| D2 | Baixa | M | `SaveChangesAsync` espalhado pelos repositórios (documento salvo via `processRepository`, processo via `refreshTokenRepository`) → centralizar no `IUnitOfWork` | Repositories/* |
| D3 | Baixa | P | `UnitOfWork` sem `ExecutionStrategy` → quebra se ligar `EnableRetryOnFailure` (Azure SQL) | `UnitOfWork.cs:10` |
| D4 | Baixa | M | DTOs de resposta vazando para o Core (repositórios retornam `*Response`, `AuthSession` usa `LoginResponse`) | `IProcessRepository.cs`, `IProcessTypeRepository.cs`, `AuthSession.cs` |
| D5 | Baixa | M | Tratamento de erro inconsistente: `OperationResult` × exceções × `null`; falhas do Identity viram 500 | `InternalUserService.cs:48`, `CustomerService.cs:46`, `AuthService`, `PreferencesService` |
| D6 | Baixa | P-M | Código duplicado: `ActorAsync`, checagem "processo é do cliente", reset+revogação, cópia limitada de stream, clamp de página, helpers de trim | Services/*, Core/* |
| D7 | Baixa | P | `Notification.Details` muda de significado por evento; visibilidade do `StepOverdue` depende do nome do enum em texto | `Notification.cs`, `ProcessParts.cs:158-163` |
| D8 | Baixa | P | Timestamps inconsistentes (`ProcessType.Deactivate` não atualiza `UpdatedAt`; `Customer` sem `UpdatedAt/By`); `RefreshActiveStep(now = null)` perigoso | `ProcessType.cs:108`, `Customer.cs`, `Process.cs:254` |
| D9 | Baixa | P | `UserAccountStore` ignora o `CancellationToken` | `UserAccountStore.cs` |

## E. Contrato da API

| # | Sev | Esf | Item | Onde |
|---|---|---|---|---|
| E1 | Média | P | CORS sem `WithExposedHeaders("Content-Disposition","Location")` → o front não lê nome do arquivo baixado nem `Location` | `ServiceCollectionExtensions.cs:176-180` |
| E2 | Baixa | P | Endpoints de workflow sem `[ProducesResponseType]`; `GenerateDocumentationFile` desligado (os `///` não chegam ao OpenAPI); sem UI (Scalar) | `ProcessesController.cs`, `MyProcessesController.cs`, csproj |
| E3 | Baixa | M | Sem versionamento (`api/v1`) | controllers |
| E4 | Baixa | P | Inconsistências: enum como inteiro aceito na query mas não no JSON; `GET preferences` → 204; dois endpoints "me" (`/auth/me` e `/me/profile`) | `Program.cs`, `MeController.cs`, `AuthController.cs` |

## F. Operação e vitrine do portfólio

| # | Sev | Esf | Item |
|---|---|---|---|
| F1 | Média | M | Migrations só rodam em Development; connection string vazia não é validada no start |
| F2 | Média | M | Arquivos em disco local dentro do app (perdidos em redeploy/container; não escala) → caminho externo/blob |
| F3 | Média | M | Worker roda em toda instância → avisos duplicados (lock `sp_getapplock` ou lease) |
| F4 | Baixa | P | Worker pode derrubar o host (OCE não-shutdown, `Interval` ≤ 0) |
| F5 | Média | M | Health checks, Docker/docker-compose, logs estruturados + request logging, `ForwardedHeaders` |
| F6 | Média | M | README (domínio, diagrama, decisões, como rodar), GitHub Actions, remover pastas `.gitkeep` vazias |

## G. Funcionalidades atuais incompletas

| # | Item |
|---|---|
| G1 | E-mail: convite com senha temporária e recuperação de senha (hoje só aparece na resposta da API) |
| G2 | Notificar a equipe quando o cliente altera contatos (há um TODO em `CustomerProfileService.cs:63-67`) |
| G3 | Reabrir processo concluído (órgão pediu algo depois da entrega) |
| G4 | Progresso do processo pai calculado a partir dos subprocessos (ex.: PGRS) |

## H. Novas funcionalidades (licenciamento ambiental) — para a próxima fase

| Sev | Item |
|---|---|
| Alta | **Licença** como entidade (nº, tipo LP/LI/LO/LAS/LAU, órgão, emissão, validade, processo e entregável vinculados) |
| Alta | **Vencimento e renovação**: alertas e prazo de 120 dias antes do vencimento (LC 140/2011, art. 14 §4º) — *validar com fonte oficial* |
| Alta | **Condicionantes**: obrigações com prazo/recorrência, evidência e status |
| Média | Dados de **protocolo no órgão** (nº SEI/processo, data, prazo de análise) |
| Média | **Empreendimento** (endereço, município/UF, CNAE, porte, potencial poluidor) — licença é do local, não do CNPJ |
| Média | **Validade de documentos** (certidões, ARTs, CTF/IBAMA) |
| Média | **Exigências do órgão** estruturadas (pausam o relógio do órgão) |
| Baixa | Taxas/boletos nas etapas de Pagamento; dashboard (status, vencidos, tempo médio por etapa) |

---

## Ordem sugerida

1. **Onda 1 – correções rápidas (P):** A4, A6, A7, A9, A12, A14, B6, C6, C9, E1, B1.
2. **Onda 2 – integridade:** A2 (concorrência), D1 (órfãos), A1 (pausa), A3, A5, A8, A10.
3. **Onda 3 – segurança:** B2, B3, B4, B5, B7, B8, B10.
4. **Onda 4 – performance/dados:** C1, C2, C3, C4, C5, C8, C7.
5. **Onda 5 – arquitetura:** D2–D9, E2–E4.
6. **Onda 6 – operação/portfólio:** F1–F6.
7. Depois: G e H.
