# Redis Cluster Chaos Test Runbook

此 runbook 用于对比 `csredis` 与 `stackexchange` 在以下场景下的表现：

- 动态缩容（`scale-in`）
- 主从切换（`failover`）
- 节点掉线（`node-down`）

## 1. 目标与指标

统一观察口径：

- 可用性：`Success/Failed`
- 性能：`QPS`、`p95/p99`
- 恢复：故障注入后到成功率恢复的时间
- 异常：客户端日志中的 timeout / connection / moved 相关错误

## 2. 前置条件

- Redis 集群与应用容器已启动
- `csredis-tests-app` 可访问 `http://127.0.0.1:50051`
- `stackexchange-tests-app` 可访问 `http://127.0.0.1:50052`

建议先确认：

```powershell
docker compose -f .\docker-compose-redis-cluster.yml ps
```

## 3. 脚本说明

使用脚本：`run-chaos-compare.ps1`

它会：

1. 分场景启动 `run-grpc-phased-stress.ps1`（suite=csredis/stackexchange）
2. 在指定延迟后注入故障
3. 等压测结束后汇总 `summary.compare.csv`
4. 生成总汇总 `summary.all.csv`

## 4. 典型执行命令

### 4.1 全场景 + 双客户端对比

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-compare.ps1 -Scenario all -Suite both
```

### 4.2 单场景：主从切换窗口

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-compare.ps1 -Scenario failover -Suite both -InjectionDelaySeconds 20 -FaultDurationSeconds 60
```

### 4.3 单场景：节点掉线（指定节点）

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-compare.ps1 -Scenario node-down -NodeDownNode redis-cluster-5 -Suite both
```

### 4.4 动态缩容模拟（停掉一个副本）

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-compare.ps1 -Scenario scale-in -ScaleInNode redis-cluster-6 -Suite both
```

### 4.5 仅演练流程（不执行）

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-compare.ps1 -Scenario all -Suite both -DryRun
```

## 5. 输出目录

默认输出到：

`artifacts/chaos-compare/run-<runId>/`

结构示例：

- `failover/summary.compare.csv`
- `node-down/summary.compare.csv`
- `scale-in/summary.compare.csv`
- `summary.all.csv`
- `events.timeline.csv`（场景时间线：readiness、注入开始/结束、suite开始/结束）
- `analysis.summary.csv`（通过分析脚本生成）
- `analysis.report.md`（通过分析脚本生成）

## 5.1 自动分析（推荐）

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-analyze.ps1 -RunId <runId>
```

可调阈值：

```powershell
powershell -ExecutionPolicy Bypass -File .\run-chaos-analyze.ps1 -RunId <runId> -SuccessRateThreshold 99.5 -P95RatioThreshold 3.0
```

## 6. 场景实现细节

- `failover`：停止并恢复 `FailoverMaster`（默认 `redis-cluster-1`）
- `node-down`：停止并恢复 `NodeDownNode`（默认 `redis-cluster-4`）
- `scale-in`：停止并恢复 `ScaleInNode`（默认 `redis-cluster-6`）

> 说明：`scale-in` 在该脚本中是“缩容窗口模拟”（通过节点下线实现），用于观测客户端在拓扑减少时的行为。若要做永久缩容（迁槽 + del-node），建议在单独维护窗口执行。

## 7. 结果判定建议

- 故障窗口成功率 >= 99.5%
- 故障解除后 30 秒内恢复到基线成功率
- 故障窗口 `p95` 不超过基线 3 倍
- 不出现持续 60 秒以上的 timeout 风暴
