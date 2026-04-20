# A Test

## csredis 
在从节点异常下线时
  FAIL redis (direct)  (【redis-cluster-1:6379/0】Status unavailable, waiting for recovery. Object reference not set to an instance of an object.)
  
 在主节点下线时:
 成功提升从节点
 
 主从节点均掉线时
   FAIL redis (direct)  (【redis-cluster-1:6379/0】Status unavailable, waiting for recovery. Object reference not set to an instance of an object.)
 【172.19.0.4:6379/0】Next recovery time: 04/06/2026 14:14:15 (Connection was not opened)
 Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object.
    at CSRedis.CSRedisClientPipe`1.PipeCommand[TReturn](String key, Func`3 handle, Func`2 parser)
    at CSRedis.CSRedisClientPipe`1.Set(String key, Object value, Int32 expireSeconds, Nullable`1 exists)
    at CsRedisRepository.<>c__DisplayClass3_0.<PipelineSetAsync>b__0() in D:\GodotProjects\godot_learning\StackExchangeTests\CsredisTests\CsRedisRepository.cs:line 32
    at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
 --- End of stack trace from previous location ---
    at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
    at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
 --- End of stack trace from previous location ---
    at Program.<<Main>$>g__RunPipelineTestAsync|0_2(CsRedisRepository repository) in D:\GodotProjects\godot_learning\StackExchangeTests\CsredisTests\Program.cs:line 111
    at Program.<Main>$(String[] args) in D:\GodotProjects\godot_learning\StackExchangeTests\CsredisTests\Program.cs:line 96
    at Program.<Main>(String[] args)
        
        
## stack exchange redis 

主从节点均掉线时， 测试循环轮询 集群状态

  OK   redis://Unspecified/redis-cluster-4:6379  ping=0.4ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  FAIL redis://Unspecified/redis-cluster-5:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-5:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/102/97/0, sync-ops: 0, async-ops: 9, serverEndpoint: redis-cluster-5:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=10,Free=32757,Min=16,Max=32767), POOL: (Threads=16,QueuedItems=0,CompletedItems=47261,Timers=23), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-6:6379  ping=0.7ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://172.19.0.5:6379  ping=0.5ms  slots=10923-16383  (standalone/cluster endpoint)

  OK   redis://172.19.0.3:6379  ping=0.6ms  slots=5461-10922  (standalone/cluster endpoint)

  OK   redis://172.19.0.7:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

  OK   redis://172.19.0.6:6379  ping=0.5ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

[MainLoop] Redis/test error (will retry): RedisServerException: CLUSTERDOWN The cluster is down

--- Test run @ 2026-04-06 14:16:56 ---

[RedisTopology] Reachable nodes (before tests):

  FAIL redis://Unspecified/redis-cluster-1:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-1:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/103/98/0, sync-ops: 0, async-ops: 2, serverEndpoint: redis-cluster-1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=6,Free=32761,Min=16,Max=32767), POOL: (Threads=16,QueuedItems=0,CompletedItems=47699,Timers=27), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-2:6379  ping=0.7ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-3:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-4:6379  ping=0.4ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  FAIL redis://Unspecified/redis-cluster-5:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-5:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/103/98/0, sync-ops: 0, async-ops: 9, serverEndpoint: redis-cluster-5:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=7,Free=32760,Min=16,Max=32767), POOL: (Threads=15,QueuedItems=0,CompletedItems=47812,Timers=28), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-6:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://172.19.0.5:6379  ping=0.7ms  slots=10923-16383  (standalone/cluster endpoint)

  OK   redis://172.19.0.3:6379  ping=0.4ms  slots=5461-10922  (standalone/cluster endpoint)

  OK   redis://172.19.0.7:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

  OK   redis://172.19.0.6:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

[MainLoop] Redis/test error (will retry): RedisServerException: CLUSTERDOWN The cluster is down

--- Test run @ 2026-04-06 14:17:09 ---

[RedisTopology] Reachable nodes (before tests):

  FAIL redis://Unspecified/redis-cluster-1:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-1:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/104/99/0, sync-ops: 0, async-ops: 2, serverEndpoint: redis-cluster-1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=9,Free=32758,Min=16,Max=32767), POOL: (Threads=16,QueuedItems=0,CompletedItems=48248,Timers=27), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-2:6379  ping=0.6ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-3:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-4:6379  ping=0.7ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  FAIL redis://Unspecified/redis-cluster-5:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-5:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/104/99/0, sync-ops: 0, async-ops: 9, serverEndpoint: redis-cluster-5:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=7,Free=32760,Min=16,Max=32767), POOL: (Threads=16,QueuedItems=0,CompletedItems=48361,Timers=27), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-6:6379  ping=0.6ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://172.19.0.5:6379  ping=0.6ms  slots=10923-16383  (standalone/cluster endpoint)

  OK   redis://172.19.0.3:6379  ping=0.5ms  slots=5461-10922  (standalone/cluster endpoint)

  OK   redis://172.19.0.6:6379  ping=0.5ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

  OK   redis://172.19.0.7:6379  ping=0.5ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

[MainLoop] Redis/test error (will retry): RedisServerException: CLUSTERDOWN The cluster is down

--- Test run @ 2026-04-06 14:17:22 ---

[RedisTopology] Reachable nodes (before tests):

  FAIL redis://Unspecified/redis-cluster-1:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-1:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/105/100/0, sync-ops: 0, async-ops: 2, serverEndpoint: redis-cluster-1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=7,Free=32760,Min=16,Max=32767), POOL: (Threads=16,QueuedItems=0,CompletedItems=48753,Timers=30), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-2:6379  ping=0.6ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-3:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://Unspecified/redis-cluster-4:6379  ping=0.4ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  FAIL redis://Unspecified/redis-cluster-5:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-5:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, last-in: 0, cur-in: 0, lm: 5/105/100/0, sync-ops: 0, async-ops: 9, serverEndpoint: redis-cluster-5:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=8,Free=32759,Min=16,Max=32767), POOL: (Threads=14,QueuedItems=0,CompletedItems=48837,Timers=28), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))

  OK   redis://Unspecified/redis-cluster-6:6379  ping=0.6ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)

  OK   redis://172.19.0.3:6379  ping=0.5ms  slots=5461-10922  (standalone/cluster endpoint)

  OK   redis://172.19.0.5:6379  ping=0.4ms  slots=10923-16383  (standalone/cluster endpoint)

  OK   redis://172.19.0.6:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

  OK   redis://172.19.0.7:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)

[MainLoop] Redis/test error (will retry): RedisServerException: CLUSTERDOWN The cluster is down


### after reshard

依旧需要监听器排除不可用的NODE

 [RedisTopology] Reachable nodes (before tests):
 
   FAIL redis://Unspecified/redis-cluster-1:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-1:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 8/761/753/0, sync-ops: 0, async-ops: 2, serverEndpoint: redis-cluster-1:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=12,Free=32755,Min=16,Max=32767), POOL: (Threads=17,QueuedItems=0,CompletedItems=340119,Timers=100), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))
 
   OK   redis://Unspecified/redis-cluster-2:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)
 
   OK   redis://Unspecified/redis-cluster-3:6379  ping=0.5ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)
 
   OK   redis://Unspecified/redis-cluster-4:6379  ping=0.4ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)
 
   FAIL redis://Unspecified/redis-cluster-5:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) redis-cluster-5:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 9/762/753/0, sync-ops: 0, async-ops: 9, serverEndpoint: redis-cluster-5:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=15,Free=32752,Min=16,Max=32767), POOL: (Threads=21,QueuedItems=0,CompletedItems=340501,Timers=114), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))
 
   OK   redis://Unspecified/redis-cluster-6:6379  ping=0.6ms  slots=(not listed in CLUSTER NODES)  (standalone/cluster endpoint)
 
   OK   redis://172.19.0.5:6379  ping=0.6ms  slots=10923-16383  (standalone/cluster endpoint)
 
   OK   redis://172.19.0.3:6379  ping=0.4ms  slots=0-10922  (standalone/cluster endpoint)
 
   OK   redis://172.19.0.7:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)
 
   FAIL redis://172.19.0.2:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) 172.19.0.2:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/762/757/0, sync-ops: 0, async-ops: 18, serverEndpoint: 172.19.0.2:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=13,Free=32754,Min=16,Max=32767), POOL: (Threads=21,QueuedItems=0,CompletedItems=340894,Timers=87), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))
 
   FAIL redis://172.19.0.4:6379  (The message timed out in the backlog attempting to send because no connection became available (3000ms) - Last Connection Exception: It was not possible to connect to the redis server(s) 172.19.0.4:6379/Interactive. ConnectTimeout, command=PING, timeout: 3000, inst: 0, qu: 0, qs: 0, aw: False, bw: CheckingForTimeout, rs: NotStarted, ws: Initializing, in: 0, last-in: 0, cur-in: 0, lm: 5/762/757/0, sync-ops: 0, async-ops: 19, serverEndpoint: 172.19.0.4:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 10 of 10 available, clientName: fe0378b2db07(SE.Redis-v2.12.8.35577), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=10,Free=32757,Min=16,Max=32767), POOL: (Threads=21,QueuedItems=0,CompletedItems=341051,Timers=69), v: 2.12.8.35577 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts))
 
   OK   redis://172.19.0.6:6379  ping=0.4ms  slots=(replica, slots on primary)  (standalone/cluster endpoint)
 
 [PipelineTest] Start
 
 [PipelineTest] pipeline:test:5e777fddd8f341e4abe03108f2edfb50:k1 => v1
 
 [PipelineTest] pipeline:test:5e777fddd8f341e4abe03108f2edfb50:k2 => v2
 
 [PipelineTest] pipeline:test:5e777fddd8f341e4abe03108f2edfb50:k3 => v3
 
 [PipelineTest] End
 
 [CacheShellTest] Start
 
 [CacheShellTest] String key: first=value-from-factory, second=value-from-factory, factoryInvocations=1 (expect 1)
 
 [CacheShellTest] Hash key/field: first=hash-from-factory, second=hash-from-factory, factoryInvocations=1 (expect 1)
 
 [CacheShellTest] End

## commands

docker exec redis-cluster-1 redis-cli CLUSTER NODES 

docker exec redis-cluster-1 redis-cli CLUSTER INFO

redis-cli --cluster reshard 192.168.56.11:6379


docker exec -it redis-cluster-1 redis-cli --cluster reshard redis-cluster-1:6379 \
  --cluster-from <REMOVE_NODE_ID> \
  --cluster-to <TARGET_MASTER_ID> \
  --cluster-slots 5461 \
  --cluster-yes
  
  docker exec -it redis-cluster-1 redis-cli --cluster reshard redis-cluster-1:6379 --cluster-from c1e7c11e52a5ac7a9a167c6541d52a372c82abd0 --cluster-to 2136a03050b48e8364c8d87e2cfab14a200e7246 --cluster-slots 5461 --cluster-yes
    
    
    docker logs -f stackexchange-tests-app
    docker logs -f csredis-tests-app
    
  
  ## link
  
  https://www.cnblogs.com/linuxk/p/10736780.html
  
  https://zhuanlan.zhihu.com/p/145186839
  
  https://bbs.huaweicloud.com/blogs/344366