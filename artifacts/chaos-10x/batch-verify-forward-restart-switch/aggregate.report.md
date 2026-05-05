# Chaos 10x Aggregate Report

- BatchId: verify-forward-restart-switch
- Iterations: 1
- Scenario: failover
- Suite: both

## failover / csredis
- Runs: 1, PASS: 0, FAIL: 1
- AvgSuccessRate: 94.84%
- AvgMixedQps: 275.31
- AvgMixedP95: 148.83 ms
- AvgMixedP99: 157.54 ms
- AvgWorstMethodP95Ratio: 1.26 x
- TopFailureClass: mixed_or_unknown

## failover / stackexchange
- Runs: 1, PASS: 0, FAIL: 1
- AvgSuccessRate: 96.45%
- AvgMixedQps: 214.33
- AvgMixedP95: 149.34 ms
- AvgMixedP99: 164.37 ms
- AvgWorstMethodP95Ratio: 1.06 x
- TopFailureClass: mixed_or_unknown

