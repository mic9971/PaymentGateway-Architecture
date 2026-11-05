# Workload and Capacity — To Confirm

Inputs: business transactions/day vs HTTP requests/day; peak windows, burst factor, retry amplification, API calls/payment, message count, callback rate, response time.

Formulas:
- Average TPS = transactions / seconds in interval
- HTTP RPS = payment TPS × API requests/payment × amplification
- In-flight requests ≈ arrival rate × average response time (Little's Law, stable system)

Do not derive peak RPS by simply dividing daily volume by 86,400. Capacity based on measured sustainable throughput, failure headroom and cost.
