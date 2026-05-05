# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; successRate=15.53% ; mixedP95=1.18 ; mixedP99=17.53 ; mixedQps=3506.58 ; worstMethodP95Ratio=0.91
- Suite stackexchange: verdict=FAIL; successRate=96.66% ; mixedP95=522.27 ; mixedP99=858.31 ; mixedQps=533.47 ; worstMethodP95Ratio=10.1

## Scenario: node-down
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.19 ; mixedP99=12.1 ; mixedQps=3535.97 ; worstMethodP95Ratio=4.79
- Suite stackexchange: verdict=FAIL; successRate=77.6% ; mixedP95=559.22 ; mixedP99=743.79 ; mixedQps=590.5 ; worstMethodP95Ratio=7.7

## Scenario: scale-in
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.44 ; mixedP99=15.26 ; mixedQps=3504.13 ; worstMethodP95Ratio=5.34
- Suite stackexchange: verdict=FAIL; successRate=77.84% ; mixedP95=559.58 ; mixedP99=812.06 ; mixedQps=576.24 ; worstMethodP95Ratio=7.47

