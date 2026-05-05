# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: node-down
- Suite csredis: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=100% ; mixedP95=9503.04 ; mixedP99=13055.96 ; mixedQps=12.76 ; worstMethodP95Ratio=53.89
- Suite stackexchange: verdict=PASS; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=100% ; mixedP95=452.27 ; mixedP99=493.18 ; mixedQps=227.12 ; worstMethodP95Ratio=1.73

