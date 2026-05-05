# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=94.84% ; mixedP95=148.83 ; mixedP99=157.54 ; mixedQps=275.31 ; worstMethodP95Ratio=1.26
- Suite stackexchange: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=96.45% ; mixedP95=149.34 ; mixedP99=164.37 ; mixedQps=214.33 ; worstMethodP95Ratio=1.06

