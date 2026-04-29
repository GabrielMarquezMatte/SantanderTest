import http from 'k6/http'
import { check } from 'k6'

export default function () {
  let res = http.get('http://localhost:5136/HackerNews/best?count=100')
  check(res, { 'success request': (r) => r.status === 200 })
}
export const options = {
  insecureSkipTLSVerify: true,
}
