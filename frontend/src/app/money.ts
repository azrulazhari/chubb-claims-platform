// Validate the original MYR text in sen; never convert it to a JavaScript number.
export function parseMyr(input: string): string | null {
  const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(input.trim());
  if (!match) return null;
  const whole = match[1].replace(/^0+/, '') || '0';
  if (whole.length > 17) return null;
  const sen = BigInt(whole) * 100n + BigInt((match[2] ?? '').padEnd(2, '0'));
  if (sen <= 0n || sen > 9223372036854775807n) return null;
  return `${sen / 100n}.${(sen % 100n).toString().padStart(2, '0')}`;
}
