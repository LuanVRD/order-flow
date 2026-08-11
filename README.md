# OrderFlow

**OrderFlow** é um projeto de demonstração prática de arquitetura de microsserviços distribuídos construído no ecossistema .NET 9. Ele simula o processamento e fluxo de vida de pedidos com mensageria assíncrona, desacoplamento de serviços e aderência aos princípios de **Clean Architecture**.

---

## 🏗️ Visão Geral da Arquitetura

O sistema é dividido em dois microsserviços principais e uma biblioteca compartilhada de contratos:

1. **Orders Service**: Responsável pelo gerenciamento do ciclo de vida de pedidos (Criação, Alteração de Status, Cancelamento).
2. **Notifications Service**: Worker responsável por consumir eventos de domínio via RabbitMQ e registrar notificações persistidas.
3. **BuildingBlocks (Messaging.Contracts)**: Contratos e envelopes de eventos distribuídos compartilhados entre microsserviços.

---

## 📁 Estrutura da Solução

```text
OrderFlow/
├── src/
│   ├── BuildingBlocks/
│   │   └── OrderFlow.Messaging.Contracts/         # Contratos de eventos desacoplados
│   └── Services/
│       ├── Orders/                                # Microsserviço de Pedidos
│       │   ├── OrderFlow.Orders.Domain/           # Entidades, Value Objects e Regras de Negócio
│       │   ├── OrderFlow.Orders.Application/      # Casos de Uso, DTOs e Interfaces
│       │   ├── OrderFlow.Orders.Infrastructure/   # EF Core, PostgreSQL e RabbitMQ Publisher
│       │   └── OrderFlow.Orders.Api/              # REST API (ASP.NET Core)
│       └── Notifications/                         # Microsserviço de Notificações
│           ├── OrderFlow.Notifications.Domain/
│           ├── OrderFlow.Notifications.Application/
│           ├── OrderFlow.Notifications.Infrastructure/
│           └── OrderFlow.Notifications.Worker/   # Worker Service consumidor do RabbitMQ
└── tests/
    ├── Orders/
    │   ├── OrderFlow.Orders.Domain.Tests/        # Testes unitários do domínio Orders
    │   ├── OrderFlow.Orders.Application.Tests/   # Testes unitários de aplicação
    │   └── OrderFlow.Orders.IntegrationTests/    # Testes de integração
    └── Notifications/
        └── OrderFlow.Notifications.Tests/        # Testes de notificações
```

---

## 🛠️ Tecnologias Utilizadas

- **.NET 9 SDK** (C# 13)
- **ASP.NET Core Web API**
- **Background Worker Service**
- **Entity Framework Core**
- **RabbitMQ** (Mensageria com Publisher/Subscriber e DLQ)
- **PostgreSQL** (Persistência relacional isolada por microsserviço)
- **xUnit** (Testes unitários e de integração)

---

## 🚀 Como Executar

### Pré-requisitos
- .NET 9 SDK instalado

### Compilação da Solução

Para restaurar dependências e compilar toda a solução:

```bash
dotnet build OrderFlow.sln
```

### Execução dos Testes

Para rodar todos os testes automatizados da solução:

```bash
dotnet test OrderFlow.sln
```

---

## 📖 Documentação Detalhada

Para detalhes aprofundados sobre decisões de design, direções de dependência entre camadas, resiliência (Retry e DLQ) e idempotência, consulte o arquivo [ARCHITECTURE.md](file:///e:/projetos/order-flow/ARCHITECTURE.md).
