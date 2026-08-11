# Arquitetura do OrderFlow

## 1. Princípios e Diretrizes

O **OrderFlow** adota os princípios de **Clean Architecture** (Arquitetura Limpa) e **Domain-Driven Design (DDD) tático**, com foco no desacoplamento entre regras de negócio, infraestrutura de persistência/mensageria e interfaces de entrada.

### 1.1 Regras de Dependência (Clean Architecture)

A direção das dependências no projeto respeita rigorosamente a regra das camadas internas não conhecerem as camadas externas:

```text
       [ Domain ]  <--- (sem dependências de outros projetos da solução)
           ^
           |
      [ Application ]  <--- (depende apenas do Domain e Messaging.Contracts)
           ^
           |
    [ Infrastructure ] <--- (depende de Application, Domain e Messaging.Contracts)
           ^
           |
     [ API / Worker ]  <--- (depende de Infrastructure e Application para composição)
```

1. **Domain (`OrderFlow.*.Domain`)**: Contém entidades, objetos de valor, exceções de domínio e enums. Não possui dependências de bibliotecas de infraestrutura (como Entity Framework Core ou RabbitMQ) nem de outros projetos da solução.
2. **Application (`OrderFlow.*.Application`)**: Contém a orquestração de casos de uso (Commands/Queries), DTOs, validadores e interfaces de repositório e serviços de mensageria.
3. **Infrastructure (`OrderFlow.*.Infrastructure`)**: Implementa as interfaces da camada Application (acesso a banco PostgreSQL via EF Core, publicação/consumo RabbitMQ, etc.).
4. **API / Worker (`OrderFlow.*.Api` e `OrderFlow.*.Worker`)**: Pontos de entrada executáveis responsável pela injeção de dependências (DI), inicialização da aplicação, middlewares e exposição de endpoints ou workers de segundo plano.
5. **BuildingBlocks (`OrderFlow.Messaging.Contracts`)**: Biblioteca compartilhada contendo apenas os contratos de DTOs/Eventos de integração que trafegam entre microsserviços.

---

## 2. Microsserviços e Fronteiras de Contexto (Bounded Contexts)

### 2.1 Orders Service
- **Responsabilidade**: Gerenciar o ciclo de vida dos pedidos (`Pending` -> `Processing` -> `Completed` ou `Cancelled`).
- **Persistência**: Banco isolado `orderflow_orders` no PostgreSQL.
- **Mensageria**: Publica eventos de integração (`OrderCreated`, `OrderStatusChanged`, `OrderCompleted`, `OrderCancelled`) no RabbitMQ após mudanças de estado.

### 2.2 Notifications Service
- **Responsabilidade**: Consumir eventos publicados pelo *Orders Service*, converter em notificações auditáveis e garantir idempotência.
- **Persistência**: Banco isolado `orderflow_notifications` no PostgreSQL (tabelas `Notifications` e `ProcessedMessages`).
- **Mensageria**: Consome da fila `orderflow.notifications` com suporte a políticas de retry e Dead Letter Queue (DLQ).

---

## 3. Padrões Distribuídos

- **Envelope Padrão de Eventos**: Todos os eventos seguem a estrutura padrão com `eventId`, `eventType`, `occurredAt`, `version` e `data`.
- **Idempotência**: O consumidor consulta e registra o `eventId` na tabela `ProcessedMessages` antes de processar, evitando duplicidade de efeitos colaterais em reentregas.
- **Tratamento de Erros e DLQ**: Falhas transitórias no consumidor são retentadas em até N vezes. Persistindo a falha, a mensagem é encaminhada para `orderflow.notifications.dlq`.
