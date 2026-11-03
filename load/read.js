import http from 'k6/http';
import { check } from 'k6';

const base = __ENV.BASE_URL || 'http://localhost:15100';

export default function () {
  const res = http.get(`${base}/models`);
  check(res, { 'status 200': (r) => r.status === 200 });
}
