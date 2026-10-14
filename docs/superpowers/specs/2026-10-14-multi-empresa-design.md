# Multi-empresa (multi-tenant) e escalabilidade horizontal — design

Data: 2026-10-14 · Status: aprovado em conversa · Repositórios: `BZ.Howly.Api`, `bz-howly-web`

## Objetivo

Um único deploy atende várias consultorias ("tenants"), com isolamento total de dados, e escala horizontalmente
(N instâncias idênticas atrás de um balanceador) sem gargalo por instância. No MVP só existe a consultoria **Bia**;
a base para o portal de super admin fica pronta, sem telas.

## Decisões

| Tema | Decisão |
|---|---|
| Identificação | **Subdomínio** (`bia.howly.app`). Dev: `bia.localhost`; `localhost` puro cai em `Tenancy:DevelopmentSlug` só em Development. |
| Isolamento | **Banco compartilhado + `TenantId`** com filtro global do EF Core e carimbo automático no insert. RLS no SQL Server fica como evolução. |
| Portal admin | É um tenant especial: slug `admin`, `IsPlatform = true`. Só papel `PlatformAdmin`; rotas de negócio respondem 404 lá. Futuras rotas em `/api/platform`. |
| Dados atuais | Eram de teste: migrations recriadas do zero (`InitialCreate`), banco local apagado uma vez. |
| Rate limit | Continua em memória por instância (2ª camada). O bloqueio de conta, no banco, vale para todas as instâncias. Redis quando houver tráfego real. |
| Arquivos | `IFileStorage` com provedor por configuração: `Disk` (dev/testes, `App_Data/files`, acessível no Explorer) e `AzureBlob` (produção). Chaves prefixadas pela consultoria. |

## 1. Fundação

- `Tenant { Id, Slug, Name, Status (Active|Suspended), IsPlatform, CreatedAt }`. Slug único, minúsculo, `[a-z0-9-]`;
  reservados: `admin` (plataforma), `api`, `www`, `app`.
- `TenantResolutionMiddleware`: lê o host (já ajustado por `ForwardedHeaders`), extrai o primeiro rótulo quando o host
  termina em um dos `Tenancy:BaseDomains` (`localhost`, `howly.app`…), busca o tenant com cache em memória de poucos
  minutos e grava em `ITenantContext` (escopo de requisição). Desconhecido → 404 problem details
  (`code: tenant_not_found`); suspenso → 403 (`code: tenant_suspended`). `/health` não exige tenant.
- Toda entidade de negócio implementa `ITenantOwned { Guid TenantId }`. O `HowlyDbContext` aplica
  `HasQueryFilter(e => e.TenantId == CurrentTenantId)` em todas e carimba `TenantId` em `Added`; gravar sem tenant
  definido lança exceção.
- Ignorar o filtro só em dois lugares explícitos: worker de prazos (um tenant por vez) e o futuro painel da plataforma.
- Índices únicos passam a incluir `TenantId` (CNPJ, login). Nome de tipo de processo continua não único (decisão de regra pendente). Todos os índices de busca
  começam por `TenantId`.
- IDs sequenciais (amigáveis ao índice clusterizado do SQL Server) via um gerador único no Core.

## 2. Contas e login

- `AppUser.TenantId` obrigatório; índice único do Identity vira `(TenantId, NormalizedUserName)`. Papéis são globais.
- O filtro global faz o `UserManager` procurar só dentro da consultoria do endereço.
- JWT ganha a claim `tid`; o `OnTokenValidated` recusa (401) token cujo `tid` difere do tenant do endereço.
- Refresh tokens e preferências têm `TenantId`; cookie de refresh continua host-only.
- Chave do rate limit de login: tenant + IP.
- Tenant de plataforma: só `PlatformAdmin` faz login; controllers de negócio respondem 404.
- Seed de dev (user-secrets): tenants `bia` e `admin`, Admin/cliente na Bia e `PlatformAdmin` no admin.
- Endpoint público `GET /api/tenant` → `{ slug, name, isPlatform }`.

## 3. Escalabilidade

- Arquivos: chave `{tenantId}/{pasta}/{id}{ext}`; provedores `Disk` e `AzureBlob`; ao falhar o `SaveChanges`, o arquivo
  recém-gravado é apagado; gravação interrompida não deixa sobra.
- Worker: trava distribuída (`sp_getapplock` no SQL Server; sem trava no SQLite dos testes), percorre os tenants ativos
  um a um com o contexto de cada um; busca só etapas vencidas ainda não avisadas.
- Banco: split query ao carregar processo; índices novos (UpdatedAt, CustomerId+UpdatedAt, ActiveStepDueAt, notificações).
- Balanceador: `ForwardedHeaders`, `/health` com checagem do banco, migrations automáticas só em Development
  (produção: migration bundle no deploy).

## 4. Front e testes

- Front: tenant pelo endereço; Vite faz proxy de `/api` preservando o host (mesma origem); `GET /api/tenant` alimenta o
  nome no login; páginas "Portal não encontrado" e "Portal suspenso"; portal admin mostra só login, perfil e aviso.
- Testes existentes: só a infraestrutura muda (requisições para `bia.localhost`); lógica intacta.
- Suíte de isolamento: invisibilidade entre tenants, token de outro tenant recusado, mesmo CNPJ em dois tenants,
  tenant desconhecido/suspenso, portal admin, worker em vários tenants, prefixo de arquivos, insert sem tenant falha.

## Fora do escopo

Telas do super admin, identidade visual e domínio próprio por consultoria, Redis, RLS, cadastros (próximo projeto).
