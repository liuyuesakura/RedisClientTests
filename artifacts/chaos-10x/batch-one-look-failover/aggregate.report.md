# Chaos 10x Aggregate Report

- BatchId: one-look-failover
- Iterations: 1
- Scenario: failover
- Suite: both

## failover / csredis
- Runs: 1, PASS: 0, FAIL: 1
- AvgSuccessRate: 88.9%
- AvgMixedQps: 14.56
- AvgMixedP95: 10030.04 ms
- AvgMixedP99: 15122.12 ms
- AvgWorstMethodP95Ratio: 82.82 x
- TopFailureClass: timeout_dominant

## failover / stackexchange
- Runs: 1, PASS: 0, FAIL: 1
- AvgSuccessRate: 95.73%
- AvgMixedQps: 10.6
- AvgMixedP95: 18542.44 ms
- AvgMixedP99: 18630.02 ms
- AvgWorstMethodP95Ratio: 201.91 x
- TopFailureClass: timeout_dominant

